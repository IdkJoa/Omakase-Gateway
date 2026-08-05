using Application.Common.RiskEngine;
using Application.Common.RiskEngine.AnomalyDetection;
using Application.Common.RiskEngine.Rules;
using Application.Common.RiskEngine.Scoring;
using Application.Common.Security;
using Application.Features.Auth;
using Infrastructure.AnomalyDetection;
using Infrastructure.GeoLocation;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Seeding;
using Infrastructure.Redis;
using Infrastructure.Resilience;
using Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registra los servicios de Infrastructure que no son gestionados directamente por Aspire.
    /// </summary>
    /// <remarks>
    /// DECISIÓN DE ARQUITECTURA — Sin repositorios:
    ///   El <see cref="OmakaseDbContext"/> se inyecta directamente en los handlers de comandos
    ///   y queries (CQRS). No existe capa de repositorio intermedia.
    ///
    /// NOTA: El propio DbContext es registrado por Aspire vía
    ///   <c>builder.AddNpgsqlDbContext&lt;OmakaseDbContext&gt;("Omakase")</c>
    ///   en el Program.cs de OG.Gateway.Api — no se registra aquí.
    ///
    /// Este método registra los servicios de infraestructura transversales:
    ///   HU-006  → Seed data al arranque (IDbSeeder)
    ///   HU-005  → Redis helpers con TTLs (IRedisSessionStore, IRateLimitStore, IBlacklistStore)
    ///   HU-009  → GeoLocation HTTP client (IGeoLocationService)
    ///   HU-017+ → Azure Key Vault client (ISecretProvider)
    /// </remarks>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services)
    {
        // HU-006
        services.AddScoped<IDbSeeder, OmakaseDbSeeder>();

        // HU-031 & T-067: Circuit Breaker para dependencias críticas (Redis & PostgreSQL)
        services.AddSingleton<IDependencyCircuitBreaker, DependencyCircuitBreaker>();

        // HU-005, T-012 & T-067: Registro de Redis protegido por Circuit Breaker (Decorador)
        services.AddSingleton<RedisService>();
        services.AddSingleton<IRedisService>(sp => new ResilientRedisService(
            sp.GetRequiredService<RedisService>(),
            sp.GetRequiredService<IDependencyCircuitBreaker>()));

        // HU-011 & T-021: GeoLocation (HTTP + caché en memoria, timeout -> fail-safe).
        // Config (URL/timeout) vía options pattern (GeoLocationOptions), no hardcode.
        services.AddMemoryCache();
        services.AddHttpClient<IGeoLocationService, GeoLocationService>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<GeoLocationOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        })
        .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        services.AddScoped<IRuleEvaluator, GeofenceRuleEvaluator>();
        services.AddScoped<IRuleEvaluator, TimeWindowRuleEvaluator>();
        services.AddSingleton<IFingerprintService, FingerprintService>(); 
        services.AddScoped<IRuleEvaluator, FingerprintRuleEvaluator>();
        services.AddScoped<ILastAccessService, LastAccessService>();
        services.AddScoped<IRuleEvaluator, ImpossibleTravelRuleEvaluator>();
        services.AddSingleton<IPolicyScoreCalculator, PolicyScoreCalculator>();
        services.AddSingleton<IRiskScoreConsolidator, RiskScoreConsolidator>();
        services.AddScoped<IServicePolicyProvider, ServicePolicyProvider>();
        services.AddScoped<IRiskConfigProvider, RiskConfigProvider>();
        services.AddOptions<AnomalyDetectionOptions>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<AnomalyDetectionOptions>>().Value);
        
        services.AddSingleton<IFeatureExtractor>(sp =>
            new FeatureExtractor(sp.GetRequiredService<AnomalyDetectionOptions>()));
        
        services.AddSingleton(sp =>
            new AnomalyModelTrainer(sp.GetRequiredService<AnomalyDetectionOptions>()));
        
        services.AddSingleton<IAnomalyModelCache, AnomalyModelCache>();
        services.AddSingleton<IProfileUpdateChannel, InMemoryProfileUpdateChannel>();
        services.AddHostedService<ProfileUpdateWorker>();
        services.AddHostedService<AnomalyRetrainWorker>();
        services.AddScoped<IUserProfileStore, UserProfileStore>();
        services.AddScoped<IAnomalyDetector, RandomizedPcaAnomalyDetector>();

        // HU-017: services.AddSingleton<ISecretProvider, KeyVaultSecretProvider>();

        services.AddScoped<IGatewayTokenService, GatewayTokenService>();
        services.AddScoped<ILoginService, LoginService>();

        // HU-046: Step-up MFA (TOTP) para client users.
        // TOTP y protector: puros/sin estado mutable -> Singleton. Stores Redis -> Singleton
        // (mismo patrón que IRedisService). Puerto de estado MFA usa DbContext -> Scoped.
        services.AddSingleton<Application.Common.Security.Mfa.ITotpService,
                              Application.Common.Security.Mfa.TotpService>();
        services.AddSingleton<Application.Common.Security.Mfa.ITotpSecretProtector, AesGcmTotpSecretProtector>();
        services.AddSingleton<Application.Common.Security.Mfa.IChallengeStore, ChallengeStore>();
        services.AddSingleton<Application.Common.Security.Mfa.IStepUpStore, StepUpStore>();
        services.AddSingleton<Application.Common.Security.Mfa.IMfaAttemptStore, MfaAttemptStore>();
        services.AddScoped<Application.Common.Security.Mfa.IUserMfaInfoProvider, UserMfaInfoProvider>();

        return services;
    }
}