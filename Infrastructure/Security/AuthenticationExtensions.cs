using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

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
                    ValidateAudience = false, // Dashboard is a public client, might not set audience
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
                        logger.LogWarning("AUDIT SEC-001: Fallo de autenticación JWT. Posible token inválido, expirado o manipulado detectado desde IP {Ip}. Detalles: {Exception}", 
                            context.HttpContext.Connection.RemoteIpAddress, 
                            context.Exception.Message);
                            
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
                    OnTokenValidated = context =>
                    {
                        var identity = context.Principal?.Identity as ClaimsIdentity;
                        if (identity != null)
                        {
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
                                        }
                                    }
                                }
                            }
                        }
                        return Task.CompletedTask;
                    }
                };
            });

        return services;
    }
}
