using System;
using System.Text.Json;
using System.Threading.Tasks;
using Application.Common.Security.Mfa;
using Infrastructure.Resilience;
using StackExchange.Redis;

namespace Infrastructure.Redis;

/// <summary>
/// Implementación Redis de <see cref="IStepUpStore"/> protegida por Circuit Breaker (HU-046 / T-104, T-105, T-107).
/// Clave <c>stepup:{userId}</c> → String JSON con TTL 10 min (SRS §7.11.2).
/// </summary>
public sealed class StepUpStore : IStepUpStore
{
    private readonly IDatabase _db;
    private readonly IDependencyCircuitBreaker? _circuitBreaker;

    public StepUpStore(IConnectionMultiplexer redis, IDependencyCircuitBreaker? circuitBreaker = null)
    {
        _db = redis.GetDatabase();
        _circuitBreaker = circuitBreaker;
    }

    internal static string GetKey(string userId) => $"stepup:{userId}";

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

    public Task SetAsync(string userId, StepUpData data, TimeSpan ttl)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("El ID de usuario no puede estar vacío.", nameof(userId));
        ArgumentNullException.ThrowIfNull(data);
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), "El TTL del step-up debe ser positivo.");

        var json = JsonSerializer.Serialize(data);
        return ExecuteAsync(() => _db.StringSetAsync(GetKey(userId), json, ttl));
    }

    public Task<StepUpData?> GetAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Task.FromResult<StepUpData?>(null);

        return ExecuteAsync(async () =>
        {
            var value = await _db.StringGetAsync(GetKey(userId));
            if (!value.HasValue)
                return null;

            try
            {
                return JsonSerializer.Deserialize<StepUpData>(value.ToString());
            }
            catch (JsonException)
            {
                return null;
            }
        });
    }
}
