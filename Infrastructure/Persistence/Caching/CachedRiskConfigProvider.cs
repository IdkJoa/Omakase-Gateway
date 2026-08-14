using Application.Common.RiskEngine;
using Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Infrastructure.Persistence.Caching;

/// <summary>
/// Decorador de <see cref="IRiskConfigProvider"/> que memoriza la fila única de
/// <c>risk_score_config</c> durante <see cref="RiskEngineCacheOptions.RiskConfigTtlSeconds"/>.
/// </summary>
/// <remarks>
/// Evita releer en cada evaluación una fila que solo cambia cuando se guarda el editor de pesos/umbrales;
/// el TTL de 5 s acota el retardo de propagación sin invalidación explícita entre Dashboard y Gateway (procesos distintos).
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
