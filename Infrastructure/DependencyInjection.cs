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

        // HU-005 & T-012: Registro del servicio unificado de Redis
        services.AddSingleton<IRedisService, RedisService>();

        // HU-011 & T-021: GeoLocation (HTTP + caché en memoria, timeout -> fail-safe).
        // Config (URL/timeout) vía options pattern (GeoLocationOptions), no hardcode.
        services.AddMemoryCache();
        services.AddHttpClient<IGeoLocationService, GeoLocationService>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<GeoLocationOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        // HU-011 & T-022: Evaluador de regla Geofencing (contrato IRuleEvaluator, O/C)
        services.AddScoped<IRuleEvaluator, GeofenceRuleEvaluator>();

        // HU-012 & T-023: Evaluador de regla Time-Window (contrato IRuleEvaluator, O/C)
        services.AddScoped<IRuleEvaluator, TimeWindowRuleEvaluator>();

        // HU-013 & T-024: Servicio de huella digital de navegador
        services.AddSingleton<IFingerprintService, FingerprintService>();

        // HU-013 & T-025: Evaluador de regla Fingerprint (contrato IRuleEvaluator, O/C)
        services.AddScoped<IRuleEvaluator, FingerprintRuleEvaluator>();

        // HU-014 & T-027: Servicio de último acceso del usuario
        services.AddScoped<ILastAccessService, Infrastructure.Persistence.LastAccessService>();

        // HU-014 & T-027: Evaluador de regla Viaje Imposible (contrato IRuleEvaluator, O/C)
        services.AddScoped<IRuleEvaluator, ImpossibleTravelRuleEvaluator>();

        // HU-015 & T-028/T-029: Motor de scoring. Puros y sin estado -> Singleton
        // (una instancia; evita asignación en heap por petición).
        services.AddSingleton<IPolicyScoreCalculator, PolicyScoreCalculator>();
        services.AddSingleton<IRiskScoreConsolidator, RiskScoreConsolidator>();

        // HU-015: Puertos de datos del motor (usan DbContext scoped -> Scoped).
        services.AddScoped<IServicePolicyProvider, ServicePolicyProvider>();
        services.AddScoped<IRiskConfigProvider, RiskConfigProvider>();

        // HU-016/017: Detección de anomalías con RandomizedPCA (ML.NET). Reemplaza al StubAnomalyDetector.
        // Config tipada (options pattern); overridable vía Configure<AnomalyDetectionOptions> en el host.
        services.AddOptions<AnomalyDetectionOptions>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<AnomalyDetectionOptions>>().Value);

        // Componentes puros/sin estado -> Singleton.
        services.AddSingleton<IFeatureExtractor>(sp =>
            new FeatureExtractor(sp.GetRequiredService<AnomalyDetectionOptions>()));
        services.AddSingleton(sp =>
            new AnomalyModelTrainer(sp.GetRequiredService<AnomalyDetectionOptions>()));
        services.AddSingleton<IAnomalyModelCache, AnomalyModelCache>();

        // Canal de actualización de perfil (fire-and-forget) + worker de persistencia asíncrona (T-033/T-034).
        services.AddSingleton<IProfileUpdateChannel, InMemoryProfileUpdateChannel>();
        services.AddHostedService<ProfileUpdateWorker>();

        // HU-017 T-035: reentrenamiento periódico (invalida caché + actualiza last_trained_at).
        services.AddHostedService<AnomalyRetrainWorker>();

        // Store de perfil (usa DbContext scoped) y detector real -> Scoped.
        services.AddScoped<IUserProfileStore, UserProfileStore>();
        services.AddScoped<IAnomalyDetector, RandomizedPcaAnomalyDetector>();

        // HU-017: services.AddSingleton<ISecretProvider, KeyVaultSecretProvider>();

        // HU-019 / T-038: Autenticación de Client Users con JWT propio del Gateway.
        services.AddScoped<IGatewayTokenService, GatewayTokenService>();
        services.AddScoped<ILoginService, LoginService>();

        return services;
    }
}