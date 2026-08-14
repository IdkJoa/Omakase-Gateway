using Application.Common.RiskEngine;
using Application.Common.RiskEngine.AnomalyDetection;
using Application.Common.RiskEngine.Rules;
using Application.Common.RiskEngine.Scoring;
using Application.Common.Security;
using Application.Features.Auth;
using Infrastructure.AnomalyDetection;
using Infrastructure.GeoLocation;
using Infrastructure.KeyVault;
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
    // Sin capa de repositorio: OmakaseDbContext se inyecta directo en los handlers CQRS.
    // El propio DbContext lo registra Aspire en Program.cs (AddNpgsqlDbContext), no aquí.
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services)
    {
        // El orden de registro es el orden de ejecución: OmakaseDbSeeder deja config/roles/admin de los
        // que depende DemoDataSeeder (access_policies.created_by, etc.); este último se autoprotege si esa base no está.
        services.AddScoped<IDbSeeder, OmakaseDbSeeder>();
        services.AddScoped<IDbSeeder, DemoDataSeeder>();

        services.AddSingleton<IDependencyCircuitBreaker, DependencyCircuitBreaker>();

        services.AddSingleton<RedisService>();
        services.AddSingleton<IRedisService>(sp => new ResilientRedisService(
            sp.GetRequiredService<RedisService>(),
            sp.GetRequiredService<IDependencyCircuitBreaker>()));

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
        // Ruta caliente del motor: decoradores de caché registrados por delante de la interfaz (Open/Closed);
        // con TTL 0 el decorador delega siempre, restaurando el comportamiento previo sin recompilar.
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
        // La sección "AnomalyDetection" no estaba enlazada: los valores quedaban en los defaults de la
        // clase pese a documentarse como options pattern, sin forma de calibrar el motor desde appsettings.
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

        // Baseline sintético reproducible: le da al modelo historial sin esperar semanas de tráfico real.
        services.AddSingleton(sp => new BehaviorBaselineBootstrapper(
            sp.GetRequiredService<IFeatureExtractor>(),
            sp.GetRequiredService<AnomalyDetectionOptions>()));
        
        services.AddSingleton<IAnomalyModelCache, AnomalyModelCache>();
        services.AddSingleton<IProfileUpdateChannel, InMemoryProfileUpdateChannel>();
        services.AddHostedService<ProfileUpdateWorker>();
        services.AddHostedService<AnomalyRetrainWorker>();
        // Memo por petición (sin TTL): el perfil se pedía dos veces en la misma evaluación (detector
        // de anomalías + penalización de cold-start); muere con el ámbito de la petición.
        services.AddScoped<UserProfileStore>();
        services.AddScoped<IUserProfileStore>(sp =>
            new Infrastructure.Persistence.Caching.RequestScopedUserProfileStore(
                sp.GetRequiredService<UserProfileStore>()));
        services.AddScoped<IAnomalyDetector, RandomizedPcaAnomalyDetector>();

        services.AddOptions<KeyVaultOptions>()
            .BindConfiguration(KeyVaultOptions.SectionName);
        services.AddSingleton<ISecretProvider, KeyVaultSecretProvider>();
        services.AddSingleton<KeyVaultStartupValidator>();

        services.AddScoped<IGatewayTokenService, GatewayTokenService>();
        services.AddScoped<ILoginService, LoginService>();

        // TOTP y protector son puros/sin estado mutable -> Singleton; stores Redis -> Singleton (como IRedisService).
        services.AddSingleton<Application.Common.Security.Mfa.ITotpService,
                              Application.Common.Security.Mfa.TotpService>();
        services.AddSingleton<Application.Common.Security.Mfa.ITotpSecretProtector, AesGcmTotpSecretProtector>();
        services.AddSingleton<Application.Common.Security.Mfa.IChallengeStore, ChallengeStore>();
        services.AddSingleton<Application.Common.Security.Mfa.IStepUpStore, StepUpStore>();
        services.AddSingleton<Application.Common.Security.Mfa.IMfaAttemptStore, MfaAttemptStore>();
        // Solo se consulta en veredicto CHALLENGE: sin caché, un ataque por volumen amplificaba la carga contra PostgreSQL.
        services.AddScoped<UserMfaInfoProvider>();
        services.AddScoped<Application.Common.Security.Mfa.IUserMfaInfoProvider>(sp =>
            new Infrastructure.Persistence.Caching.CachedUserMfaInfoProvider(
                sp.GetRequiredService<UserMfaInfoProvider>(),
                sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Infrastructure.Persistence.Caching.RiskEngineCacheOptions>>()));

        return services;
    }
}