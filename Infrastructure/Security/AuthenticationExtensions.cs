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

                // Seguro por defecto: la metadata OIDC (incluidas las claves de firma) SOLO se
                // descarga por HTTPS salvo que se desactive explícitamente. Estaba fijo en false,
                // así que en cualquier despliegue real un atacante en la red podía servir su propio
                // documento de descubrimiento y con él sus propias claves: token forjado aceptado.
                // Development lo pone en false porque el Keycloak del AppHost habla HTTP plano.
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

                // Map Keycloak realm roles to standard ASP.NET Role claims
                options.Events = new JwtBearerEvents
                {
                    // Un fallo de validación de token NO es un fallo de Keycloak. Este handler se
                    // dispara con cualquier token inválido —expirado, malformado, firma ajena— que
                    // es exactamente lo que un atacante envía a propósito.
                    //
                    // Antes, cada uno de esos fallos: (1) marcaba el span como "Dependency failure:
                    // Keycloak", falseando la señal de degradación de HU-032; y (2) escribía una fila
                    // SÍNCRONA en audit_logs con Verdict=Block y PolicyScore/AnomalyScore/RiskScore=100.
                    //
                    // Eso último era el problema serio, por tres motivos:
                    //   · Fabricaba puntuaciones de riesgo que ningún motor calculó. Esas filas
                    //     alimentan las métricas de HU-021, el explorador de HU-022 y la calibración
                    //     de HU-035, contaminando las tres con datos inventados.
                    //   · Un INSERT síncrono por token inválido, dentro del pipeline de autenticación,
                    //     es un amplificador de carga trivial: basta con inundar de tokens basura.
                    //   · La etiqueta "SEC-001" no traza a ningún requisito del SRS ni del backlog.
                    //
                    // El evento de seguridad se sigue registrando como log estructurado (va a Loki y
                    // es consultable); audit_logs queda reservado a evaluaciones reales del motor.
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JwtBearerHandler>>();
                        var logSanitizer = context.HttpContext.RequestServices.GetService<Application.Common.Security.ILogSanitizer>()
                            ?? new Application.Common.Security.LogSanitizer();

                        var ip = logSanitizer.Sanitize(context.HttpContext.Connection.RemoteIpAddress?.ToString());
                        var detail = logSanitizer.Sanitize(context.Exception.Message);

                        // SecurityTokenException y sus derivadas (expirado, firma inválida, emisor
                        // ajeno...) significan "token malo", no "Keycloak caído". Cualquier otra cosa
                        // —fallo al descargar la metadata OIDC, timeout, error de transporte— sí es
                        // una degradación real de la dependencia (HU-032).
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
