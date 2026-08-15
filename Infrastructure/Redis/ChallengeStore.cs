using System;
using System.Text.Json;
using System.Threading.Tasks;
using Application.Common.Security.Mfa;
using Infrastructure.Resilience;
using StackExchange.Redis;

namespace Infrastructure.Redis;

// TTL de 2-5 min impuesto por SRS §7.11.2; el llamador decide el valor exacto, no se valida aquí.
public sealed class ChallengeStore : IChallengeStore
{
    private readonly IDatabase _db;
    private readonly IDependencyCircuitBreaker? _circuitBreaker;

    public ChallengeStore(IConnectionMultiplexer redis, IDependencyCircuitBreaker? circuitBreaker = null)
    {
        _db = redis.GetDatabase();
        _circuitBreaker = circuitBreaker;
    }

    internal static string GetKey(Guid challengeId) => $"challenge:{challengeId:D}";

    private Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        return _circuitBreaker is not null 
            ? _circuitBreaker.ExecuteRedisAsync(action) 
            : action();
    }

    private Task ExecuteAsync(Func<Task> action)
    {
        return _circuitBreaker is not null 
            ? _circuitBreaker.ExecuteRedisAsync(action) 
            : action();
    }

    public Task StoreAsync(Guid challengeId, ChallengeData data, TimeSpan ttl)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), "El TTL del desafío debe ser positivo.");

        var json = JsonSerializer.Serialize(data);
        return ExecuteAsync(() => _db.StringSetAsync(GetKey(challengeId), json, ttl));
    }

    public Task<ChallengeData?> GetAsync(Guid challengeId)
    {
        return ExecuteAsync(async () =>
        {
            var value = await _db.StringGetAsync(GetKey(challengeId));
            if (!value.HasValue)
                return null;

            try
            {
                return JsonSerializer.Deserialize<ChallengeData>(value.ToString());
            }
            catch (JsonException)
            {
                return null;
            }
        });
    }

    public Task RemoveAsync(Guid challengeId)
    {
        return ExecuteAsync(() => _db.KeyDeleteAsync(GetKey(challengeId)));
    }
}
