using System;
using System.Threading.Tasks;
using Application.Common.Security.Mfa;
using StackExchange.Redis;

namespace Infrastructure.Redis;

/// <summary>
/// Implementación Redis de <see cref="IMfaAttemptStore"/> (HU-046 / T-104).
/// Clave <c>mfaattempts:{userId}</c> → contador con TTL de ventana (SRS §7.11.2),
/// mismo patrón INCR + TTL del rate limiting (T-020).
/// </summary>
public sealed class MfaAttemptStore : IMfaAttemptStore
{
    private readonly IDatabase _db;

    public MfaAttemptStore(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    internal static string GetKey(string userId) => $"mfaattempts:{userId}";

    public async Task<long> IncrementAsync(string userId, TimeSpan window)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("El ID de usuario no puede estar vacío.", nameof(userId));

        var key = GetKey(userId);
        var count = await _db.StringIncrementAsync(key);

        if (count == 1)
            await _db.KeyExpireAsync(key, window);

        return count;
    }

    public async Task ResetAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        await _db.KeyDeleteAsync(GetKey(userId));
    }
}
