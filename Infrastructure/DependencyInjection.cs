using Application.Common.Security;
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

        // HU-009: services.AddHttpClient<IGeoLocationService, GeoLocationService>();

        // HU-017: services.AddSingleton<ISecretProvider, KeyVaultSecretProvider>();

        return services;
    }
}