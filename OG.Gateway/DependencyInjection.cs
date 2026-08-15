using Application.Common.Audit;
using Application.Common.Mediator;
using Application.Common.Options;
using Application.Common.RiskEngine.Commands;
using Application.Common.Security;
using Application.Common.Security.Mfa;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Application;

public static class DependencyInjection
{
    public static IServiceCollection AddGatewayApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IMediator, Mediator>();
        services.AddScoped<IRequestHandler<EvaluateRiskCommand, RiskEvaluationResult>, EvaluateRiskHandler>();

        services.AddSingleton<ILogSanitizer, LogSanitizer>();
        services.AddSingleton<IAuditChannel, InMemoryAuditChannel>();

        services.Configure<AuditChannelOptions>(configuration.GetSection(AuditChannelOptions.SectionName));
        services.Configure<RateLimitingOptions>(configuration.GetSection(RateLimitingOptions.SectionName));
        services.Configure<MfaOptions>(configuration.GetSection(MfaOptions.SectionName));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        return services;
    }
}
