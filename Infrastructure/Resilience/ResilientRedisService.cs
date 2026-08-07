using Application.Common.Security;

namespace Infrastructure.Resilience;

/// <summary>
/// Decorador de IRedisService protegido por el Circuit Breaker de Redis (T-067 / HU-031).
/// Enruta cada llamada a Redis a través de IDependencyCircuitBreaker.
/// </summary>
public sealed class ResilientRedisService : IRedisService
{
    private readonly IRedisService _inner;
    private readonly IDependencyCircuitBreaker _circuitBreaker;

    public ResilientRedisService(IRedisService inner, IDependencyCircuitBreaker circuitBreaker)
    {
        _inner = inner;
        _circuitBreaker = circuitBreaker;
    }


    public Task AddToBlacklistAsync(string jti, TimeSpan expiration)
    {
        return _circuitBreaker.ExecuteRedisAsync(() => _inner.AddToBlacklistAsync(jti, expiration));
    }

    public Task<bool> IsBlacklistedAsync(string jti)
    {
        return _circuitBreaker.ExecuteRedisAsync(() => _inner.IsBlacklistedAsync(jti));
    }

    public Task CacheProfileAsync(string userId, string jsonProfile, TimeSpan ttl)
    {
        return _circuitBreaker.ExecuteRedisAsync(() => _inner.CacheProfileAsync(userId, jsonProfile, ttl));
    }

    public Task<string?> GetCachedProfileAsync(string userId)
    {
        return _circuitBreaker.ExecuteRedisAsync(() => _inner.GetCachedProfileAsync(userId));
    }

    public Task StoreFingerprintAsync(string userId, string fingerprintHash, TimeSpan ttl)
    {
        return _circuitBreaker.ExecuteRedisAsync(() => _inner.StoreFingerprintAsync(userId, fingerprintHash, ttl));
    }

    public Task<List<string>> GetFingerprintsAsync(string userId)
    {
        return _circuitBreaker.ExecuteRedisAsync(() => _inner.GetFingerprintsAsync(userId));
    }

    public Task<long> IncrementRateLimitAsync(string ip, TimeSpan window)
    {
        return _circuitBreaker.ExecuteRedisAsync(() => _inner.IncrementRateLimitAsync(ip, window));
    }


    public Task PublishAsync(string channel, string message)
    {
        return _circuitBreaker.ExecuteRedisAsync(() => _inner.PublishAsync(channel, message));
    }

    public Task SubscribeAsync(string channel, Action<string, string> handler)
    {
        return _circuitBreaker.ExecuteRedisAsync(() => _inner.SubscribeAsync(channel, handler));
    }
}
