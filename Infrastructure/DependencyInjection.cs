using Application.Common.RiskEngine.Rules;
using Application.Common.Security;
using Infrastructure.GeoLocation;
using Infrastructure.Persistence.Seeding;
using Infrastructure.Redis;
using Microsoft.Extensions.DependencyInjection;

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

        // HU-011 & T-021: GeoLocation (HTTP + caché en memoria, timeout 2s -> fail-safe null)
        services.AddMemoryCache();
        services.AddHttpClient<IGeoLocationService, GeoLocationService>(client =>
        {
            client.BaseAddress = new Uri("http://ip-api.com/");
            client.Timeout = TimeSpan.FromSeconds(2);
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

        // HU-017: services.AddSingleton<ISecretProvider, KeyVaultSecretProvider>();

        return services;
    }
}