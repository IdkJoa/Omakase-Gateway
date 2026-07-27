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
    /// <summary>
    /// Registra los servicios de aplicación, mediador, comandos del motor de riesgo y opciones de la capa Application (OG.Gateway).
    /// </summary>
    public static IServiceCollection AddGatewayApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Mediador & Comandos del Motor de Riesgo 
        services.AddScoped<IMediator, Mediator>();
        services.AddScoped<IRequestHandler<EvaluateRiskCommand, RiskEvaluationResult>, EvaluateRiskHandler>();

        // ── Auditoría Asíncrona & Sanitización de Seguridad 
        services.AddSingleton<ILogSanitizer, LogSanitizer>();
        services.AddSingleton<IAuditChannel, InMemoryAuditChannel>();

        // ── Options Pattern (Configuración Tipada de la Capa de Aplicación) 
        services.Configure<AuditChannelOptions>(configuration.GetSection(AuditChannelOptions.SectionName));
        services.Configure<RateLimitingOptions>(configuration.GetSection(RateLimitingOptions.SectionName));
        services.Configure<MfaOptions>(configuration.GetSection(MfaOptions.SectionName));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        return services;
    }
}
