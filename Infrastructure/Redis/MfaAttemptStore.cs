using System;
using System.Threading.Tasks;
using Application.Common.Security.Mfa;
using Infrastructure.Resilience;
using StackExchange.Redis;

namespace Infrastructure.Redis;

/// <summary>
/// Implementación Redis de <see cref="IMfaAttemptStore"/> protegida por Circuit Breaker (HU-046 / T-104, T-107).
/// Clave <c>mfaattempts:{userId}</c> → contador con TTL de ventana.
/// </summary>
public sealed class MfaAttemptStore : IMfaAttemptStore
{
    private readonly IDatabase _db;
    private readonly IDependencyCircuitBreaker? _circuitBreaker;

    public MfaAttemptStore(IConnectionMultiplexer redis, IDependencyCircuitBreaker? circuitBreaker = null)
    {
        _db = redis.GetDatabase();
        _circuitBreaker = circuitBreaker;
    }

    internal static string GetKey(string userId) => $"mfaattempts:{userId}";

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

    public Task<long> IncrementAsync(string userId, TimeSpan window)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("El ID de usuario no puede estar vacío.", nameof(userId));

        return ExecuteAsync(async () =>
        {
            var key = GetKey(userId);
            var count = await _db.StringIncrementAsync(key);

            if (count == 1)
                await _db.KeyExpireAsync(key, window);

            return count;
        });
    }

    public Task ResetAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Task.CompletedTask;

        return ExecuteAsync(() => _db.KeyDeleteAsync(GetKey(userId)));
    }
}
