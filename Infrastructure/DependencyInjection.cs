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
        // Ruta caliente del motor (T-072). Se registran las implementaciones reales por su tipo
        // concreto y los decoradores de caché por delante de la interfaz: ninguna clase existente
        // cambia (Open/Closed) y con TTL 0 el decorador delega siempre, restaurando el
        // comportamiento previo sin recompilar. Ver RiskEngineCacheOptions para las mediciones.
        services.AddOptions<Infrastructure.Persistence.Caching.RiskEngineCacheOptions>()
            .BindConfiguration(Infrastructure.Persistence.Caching.RiskEngineCacheOptions.SectionName);

        services.AddScoped<ServicePolicyProvider>();
        services.AddScoped<IServicePolicyProvider>(sp =>
            new Infrastructure.Persistence.Caching.CachedServicePolicyProvider(
                sp.GetRequiredService<ServicePolicyProvider>(),
                sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Infrastructure.Persistence.Caching.RiskEngineCacheOptions>>()));

        services.AddScoped<RiskConfigProvider>();
        services.AddScoped<IRiskConfigProvider>(sp =>
            new Infrastructure.Persistence.Caching.CachedRiskConfigProvider(
                sp.GetRequiredService<RiskConfigProvider>(),
                sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Infrastructure.Persistence.Caching.RiskEngineCacheOptions>>()));
        // La seccion "AnomalyDetection" NO estaba enlazada: pese a documentarse como options pattern,
        // los valores quedaban clavados a los defaults de la clase y no habia forma de calibrar el motor
        // desde appsettings (necesario para HU-035). Se enlaza y se valida al arrancar.
        services.AddOptions<AnomalyDetectionOptions>()
            .BindConfiguration(AnomalyDetectionOptions.SectionName)
            .Validate(o => o.PcaRank >= 1 && o.PcaRank < AnomalyFeatureVector.Dimension,
                $"AnomalyDetection:PcaRank debe estar en [1, {AnomalyFeatureVector.Dimension - 1}]: "
                + "el rango del PCA tiene que ser menor que la dimension del vector de features.")
            .Validate(o => o.MinTrainingSamples >= 1,
                "AnomalyDetection:MinTrainingSamples debe ser >= 1.")
            .Validate(o => o.FrequencySaturation >= 1,
                "AnomalyDetection:FrequencySaturation debe ser >= 1 (es divisor de la frecuencia).")
            .Validate(o => o.FrequencyWindowMinutes >= 1,
                "AnomalyDetection:FrequencyWindowMinutes debe ser >= 1.")
            .Validate(o => o.TrainingWindowMax >= o.MinTrainingSamples,
                "AnomalyDetection:TrainingWindowMax no puede ser menor que MinTrainingSamples: "
                + "la ventana nunca alcanzaria el minimo para entrenar.")
            .ValidateOnStart();

        services.AddSingleton(sp => sp.GetRequiredService<IOptions<AnomalyDetectionOptions>>().Value);

        services.AddSingleton<IFeatureExtractor>(sp =>
            new FeatureExtractor(sp.GetRequiredService<AnomalyDetectionOptions>()));

        services.AddSingleton(sp =>
            new AnomalyModelTrainer(sp.GetRequiredService<AnomalyDetectionOptions>()));

        // T-089: baseline sintetico reproducible para que el modelo tenga historial que aprender
        // sin esperar semanas de trafico real (ver BehaviorBaselineBootstrapper).
        services.AddSingleton(sp => new BehaviorBaselineBootstrapper(
            sp.GetRequiredService<IFeatureExtractor>(),
            sp.GetRequiredService<AnomalyDetectionOptions>()));
        
        services.AddSingleton<IAnomalyModelCache, AnomalyModelCache>();
        services.AddSingleton<IProfileUpdateChannel, InMemoryProfileUpdateChannel>();
        services.AddHostedService<ProfileUpdateWorker>();
        services.AddHostedService<AnomalyRetrainWorker>();
        // Memo por petición: el perfil se pedía dos veces en la misma evaluación (detector de
        // anomalías + penalización de cold-start). Sin TTL ni ventana de obsolescencia — el memo
        // muere con el ámbito de la petición.
        services.AddScoped<UserProfileStore>();
        services.AddScoped<IUserProfileStore>(sp =>
            new Infrastructure.Persistence.Caching.RequestScopedUserProfileStore(
                sp.GetRequiredService<UserProfileStore>()));
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
        // Solo se consulta cuando el veredicto es CHALLENGE, así que un ataque por volumen —que por
        // definición produce desafíos— amplificaba la carga contra PostgreSQL. Cacheado (T-072).
        services.AddScoped<UserMfaInfoProvider>();
        services.AddScoped<Application.Common.Security.Mfa.IUserMfaInfoProvider>(sp =>
            new Infrastructure.Persistence.Caching.CachedUserMfaInfoProvider(
                sp.GetRequiredService<UserMfaInfoProvider>(),
                sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Infrastructure.Persistence.Caching.RiskEngineCacheOptions>>()));

        return services;
    }
}