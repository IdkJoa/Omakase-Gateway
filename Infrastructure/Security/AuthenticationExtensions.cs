using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Domain.Entities;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Security;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddOmakaseAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = configuration["Authentication:Keycloak:Authority"] 
                    ?? "http://localhost:8080/realms/omakase-gateway";
                
                // Set to false for local development (Keycloak on HTTP)
                options.RequireHttpsMetadata = false; 
                
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Authority,
                    ValidateAudience = false, // Cliente público: la audiencia se acota por el claim `azp` en OnTokenValidated (abajo).
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "preferred_username"
                };

                // Map Keycloak realm roles to standard ASP.NET Role claims
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = async context =>
                    {
                        var logger = context.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JwtBearerHandler>>();
                        var logSanitizer = context.HttpContext.RequestServices.GetService<Application.Common.Security.ILogSanitizer>()
                            ?? new Application.Common.Security.LogSanitizer();
                        logger.LogWarning("AUDIT SEC-001: Fallo de autenticación JWT. Posible token inválido, expirado o manipulado detectado desde IP {Ip}. Detalles: {Exception}", 
                            logSanitizer.Sanitize(context.HttpContext.Connection.RemoteIpAddress?.ToString()), 
                            logSanitizer.Sanitize(context.Exception.Message));
                            
                        try
                        {
                            var dbContext = context.HttpContext.RequestServices.GetRequiredService<Infrastructure.OmakaseDbContext>();
                            var auditLog = new Domain.Entities.AuditLog
                            {
                                Id = Domain.ValueObjects.AuditLogId.New(),
                                EvaluationId = Guid.NewGuid(),
                                SourceIp = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                                UserAgent = context.HttpContext.Request.Headers.UserAgent.ToString(),
                                Verdict = Domain.Entities.Verdict.Block,
                                PolicyScore = 100,
                                AnomalyScore = 100,
                                RiskScore = 100,
                                TriggeredRules = JsonDocument.Parse("[\"INVALID_TOKEN\"]")
                            };
                            dbContext.AuditLogs.Add(auditLog);
                            await dbContext.SaveChangesAsync();
                        }
                        catch (System.Exception ex)
                        {
                            logger.LogError("Error guardando auditoría de seguridad en BD: {Msg}", ex.Message);
                        }
                    },
                    OnTokenValidated = async context =>
                    {
                        var identity = context.Principal?.Identity as ClaimsIdentity;
                        if (identity != null)
                        {
                            // Hardening: se acota la audiencia por el claim `azp` (authorized party) —
                            // solo se aceptan tokens emitidos PARA este cliente. Es la forma recomendada
                            // por Keycloak de validar la audiencia en un cliente público sin depender de
                            // un audience-mapper del realm. Fail-closed ante un azp ajeno.
                            var expectedClient = configuration["Authentication:Keycloak:ClientId"] ?? "omakase-dashboard";
                            var azp = identity.FindFirst("azp")?.Value;
                            if (!string.IsNullOrEmpty(azp) && !string.Equals(azp, expectedClient, StringComparison.Ordinal))
                            {
                                context.Fail($"Token azp '{azp}' no autorizado para el cliente '{expectedClient}'.");
                                return;
                            }

                            var sub = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                      ?? identity.FindFirst("sub")?.Value;
                            var username = identity.FindFirst("preferred_username")?.Value 
                                           ?? identity.Name;

                            var rolesList = new List<string>();
                            var realmAccessClaim = identity.FindFirst("realm_access")?.Value;
                            if (!string.IsNullOrEmpty(realmAccessClaim))
                            {
                                using var doc = JsonDocument.Parse(realmAccessClaim);
                                if (doc.RootElement.TryGetProperty("roles", out var rolesElement))
                                {
                                    foreach (var role in rolesElement.EnumerateArray())
                                    {
                                        var roleValue = role.GetString();
                                        if (!string.IsNullOrEmpty(roleValue))
                                        {
                                            identity.AddClaim(new Claim(ClaimTypes.Role, roleValue));
                                            rolesList.Add(roleValue);
                                        }
                                    }
                                }
                            }

                            // JIT Sync to local PostgreSQL users table
                            if (!string.IsNullOrEmpty(sub) && !string.IsNullOrEmpty(username))
                            {
                                try
                                {
                                    var db = context.HttpContext.RequestServices.GetRequiredService<OmakaseDbContext>();
                                    
                                    // 1. Buscar si el usuario ya existe por KeycloakSub o por Username
                                    var existingUser = await db.Users
                                        .Include(u => u.UserRoles)
                                        .FirstOrDefaultAsync(u => u.KeycloakSub == sub || u.Username == username);

                                    if (existingUser == null)
                                    {
                                        // Crear nuevo Security Officer
                                        var typedUserId = UserId.New();
                                        var newUser = new User
                                        {
                                            Id = typedUserId,
                                            Username = username,
                                            Type = UserType.SecurityOfficer,
                                            PasswordHash = null!,
                                            KeycloakSub = sub,
                                            IsActive = true,
                                            CreatedAt = DateTimeOffset.UtcNow
                                        };
                                        db.Users.Add(newUser);

                                        // Mapear los roles desde Keycloak a la DB local
                                        foreach (var roleName in rolesList)
                                        {
                                            var dbRole = await db.Roles.FirstOrDefaultAsync(r => r.Name.ToUpper() == roleName.ToUpper());
                                            if (dbRole != null)
                                            {
                                                db.UserRoles.Add(new UserRole
                                                {
                                                    Id = UserRoleId.New(),
                                                    UserId = typedUserId,
                                                    RoleId = dbRole.Id,
                                                    AssignedAt = DateTimeOffset.UtcNow
                                                });
                                            }
                                        }
                                        await db.SaveChangesAsync();
                                    }
                                    else
                                    {
                                        // Sincronizar KeycloakSub por si se creó vía seeder sin Sub
                                        bool updated = false;
                                        if (existingUser.KeycloakSub != sub)
                                        {
                                            existingUser.KeycloakSub = sub;
                                            updated = true;
                                        }
                                        if (updated)
                                        {
                                            db.Users.Update(existingUser);
                                            await db.SaveChangesAsync();
                                        }
                                    }
                                }
                                catch (System.Exception ex)
                                {
                                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<JwtBearerHandler>>();
                                    logger.LogError("Error en sincronización JIT de usuario Keycloak: {Exception}", ex.Message);
                                }
                            }
                        }
                    }
                };
            });

        return services;
    }
}
