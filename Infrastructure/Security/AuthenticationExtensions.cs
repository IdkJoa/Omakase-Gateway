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

                // Seguro por defecto (antes estaba fijo en false): sin HTTPS, un atacante en la red
                // podía servir su propio documento de descubrimiento OIDC y sus propias claves de
                // firma. Development lo desactiva porque el Keycloak del AppHost habla HTTP plano.
                options.RequireHttpsMetadata =
                    configuration.GetValue("Authentication:Keycloak:RequireHttpsMetadata", true);
                
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Authority,
                    ValidateAudience = false, // Cliente público: la audiencia se acota por el claim `azp` en OnTokenValidated (abajo).
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "preferred_username"
                };

                options.Events = new JwtBearerEvents
                {
                    // Un token inválido (expirado, malformado, firma ajena) NO es un fallo de Keycloak.
                    // Antes cada fallo de validación falseaba la señal de degradación de HU-032 y
                    // escribía una fila síncrona en audit_logs con scores inventados (100), contaminando
                    // las métricas de HU-021/HU-022/HU-035 y sirviendo de amplificador de carga trivial.
                    // Ahora solo se deja log estructurado; audit_logs queda para evaluaciones reales.
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JwtBearerHandler>>();
                        var logSanitizer = context.HttpContext.RequestServices.GetService<Application.Common.Security.ILogSanitizer>()
                            ?? new Application.Common.Security.LogSanitizer();

                        var ip = logSanitizer.Sanitize(context.HttpContext.Connection.RemoteIpAddress?.ToString());
                        var detail = logSanitizer.Sanitize(context.Exception.Message);

                        // SecurityTokenException significa "token malo"; cualquier otra excepción
                        // (fallo al descargar metadata OIDC, timeout, transporte) es degradación real
                        // de la dependencia (HU-032).
                        var esTokenInvalido = context.Exception is SecurityTokenException;

                        if (esTokenInvalido)
                        {
                            logger.LogWarning(
                                "Fallo de autenticación JWT: token inválido, expirado o manipulado desde IP {Ip}. Detalles: {Exception}",
                                ip, detail);
                        }
                        else
                        {
                            logger.LogError(
                                "Degradación de Keycloak: no se pudo validar el token por un fallo de la dependencia (IP {Ip}). Detalles: {Exception}",
                                ip, detail);

                            var activity = System.Diagnostics.Activity.Current;
                            if (activity != null)
                            {
                                activity.SetStatus(System.Diagnostics.ActivityStatusCode.Error, "Dependency failure: Keycloak");
                                activity.SetTag("error", true);
                                activity.SetTag("dependency.name", "Keycloak");
                            }
                        }

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        var identity = context.Principal?.Identity as ClaimsIdentity;
                        if (identity != null)
                        {
                            // Hardening: acota la audiencia por el claim `azp` (authorized party) — forma
                            // recomendada por Keycloak para validar audiencia en un cliente público sin
                            // audience-mapper del realm. Fail-closed ante un azp ajeno.
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

                            // Alta JIT en la tabla local de usuarios (users)
                            if (!string.IsNullOrEmpty(sub) && !string.IsNullOrEmpty(username))
                            {
                                try
                                {
                                    var db = context.HttpContext.RequestServices.GetRequiredService<OmakaseDbContext>();

                                    var existingUser = await db.Users
                                        .Include(u => u.UserRoles)
                                        .FirstOrDefaultAsync(u => u.KeycloakSub == sub || u.Username == username);

                                    if (existingUser == null)
                                    {
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
