using Application.Common.RiskEngine;
using Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Infrastructure.Persistence.Caching;

/// <summary>
/// Decorador de <see cref="IRiskConfigProvider"/> que memoriza la fila única de
/// <c>risk_score_config</c> durante <see cref="RiskEngineCacheOptions.RiskConfigTtlSeconds"/> (T-072).
/// </summary>
/// <remarks>
/// Medido con 1.000 muestras bajo 100 VUs sostenidos: <b>8,16 ms de media (12,9 % del total)</b>
/// gastados en releer en cada evaluación una fila singleton que solo cambia cuando un Security
/// Officer guarda el editor de pesos y umbrales (HU-025).
/// <para>
/// El criterio de HU-025 pide que «las siguientes evaluaciones usen el nuevo umbral». Con el TTL
/// por defecto de 5 s eso se cumple con un retardo acotado y sin invalidación explícita entre
/// procesos (el Dashboard y el Gateway son procesos distintos). Con TTL 0 se recupera la lectura
/// directa en cada evaluación.
/// </para>
/// </remarks>
public sealed class CachedRiskConfigProvider : IRiskConfigProvider
{
    private const string CacheKey = "risk-config:singleton";

    private readonly IRiskConfigProvider _inner;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;

    public CachedRiskConfigProvider(
        IRiskConfigProvider inner, IMemoryCache cache, IOptions<RiskEngineCacheOptions> options)
    {
        _inner = inner;
        _cache = cache;
        _ttl = TimeSpan.FromSeconds(options.Value.RiskConfigTtlSeconds);
    }

    /// <inheritdoc/>
    public Task<RiskScoreConfig> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_ttl <= TimeSpan.Zero)
            return _inner.GetAsync(cancellationToken);

        return _cache.GetOrCreateAsync(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _ttl;
            return _inner.GetAsync(cancellationToken);
        })!;
    }
}
