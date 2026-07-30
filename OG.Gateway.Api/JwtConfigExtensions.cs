using System.Text;
using Application.Common.Options;
using Application.Common.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace OmakaseGateway.Api;

public static class JwtConfigExtensions
{
    public static IServiceCollection AddGatewayAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // FIX HU-046/HU-019: sin esto, el mapeo de claims entrantes renombra "sub" a
                // ClaimTypes.NameIdentifier y el RiskEvaluationMiddleware (que lee "sub")
                // nunca resolvería la identidad del client user.
                options.MapInboundClaims = false;

                // Defer the configuration to the execution time so we can resolve the registered JwtOptions
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var jwtOptions = context.HttpContext.RequestServices.GetRequiredService<IOptions<JwtOptions>>().Value;
                        
                        var keyBytes = Encoding.UTF8.GetBytes(jwtOptions.SecretKey);
                        context.Options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidIssuer = jwtOptions.Issuer,
                            ValidateAudience = true,
                            ValidAudience = jwtOptions.Audience,
                            ValidateLifetime = true,
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                            ClockSkew = TimeSpan.Zero
                        };
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        var redisService = context.HttpContext.RequestServices.GetRequiredService<IRedisService>();
                        var jti = context.Principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
                        
                        if (!string.IsNullOrEmpty(jti))
                        {
                            var isBlacklisted = await redisService.IsBlacklistedAsync(jti);
                            if (isBlacklisted)
                            {
                                context.Fail("Token has been revoked.");
                            }
                        }
                    }
                };
            });

        services.AddAuthorization();
        return services;
    }
}
