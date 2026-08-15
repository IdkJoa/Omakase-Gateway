using Application.Common.Security.Mfa;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Infrastructure.Persistence.Caching;

/// <summary>
/// Decorador de <see cref="IUserMfaInfoProvider"/> que memoriza el estado MFA por usuario durante
/// <see cref="RiskEngineCacheOptions.UserMfaInfoTtlSeconds"/>.
/// </summary>
/// <remarks>
/// Este proveedor solo se invoca en veredicto CHALLENGE (SRS §3.6), así que sin caché un ataque por volumen
/// amplifica la carga contra la base de datos; los campos cacheados cambian con el enrolamiento, no con el tráfico.
/// </remarks>
public sealed class CachedUserMfaInfoProvider : IUserMfaInfoProvider
{
    private readonly IUserMfaInfoProvider _inner;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;

    public CachedUserMfaInfoProvider(
        IUserMfaInfoProvider inner, IMemoryCache cache, IOptions<RiskEngineCacheOptions> options)
    {
        _inner = inner;
        _cache = cache;
        _ttl = TimeSpan.FromSeconds(options.Value.UserMfaInfoTtlSeconds);
    }

    /// <inheritdoc/>
    public Task<UserMfaInfo?> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (_ttl <= TimeSpan.Zero)
            return _inner.GetAsync(userId, cancellationToken);

        return _cache.GetOrCreateAsync($"mfa-info:{userId}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _ttl;
            return _inner.GetAsync(userId, cancellationToken);
        });
    }
}
