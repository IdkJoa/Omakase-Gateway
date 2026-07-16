using System;
using System.Text.Json;
using System.Threading.Tasks;
using Application.Common.Security.Mfa;
using StackExchange.Redis;

namespace Infrastructure.Redis;

/// <summary>
/// Implementación Redis de <see cref="IStepUpStore"/> (HU-046 / T-104, T-105).
/// Clave <c>stepup:{userId}</c> → String JSON con TTL 10 min (SRS §7.11.2),
/// ligada a la huella del dispositivo que completó el desafío.
/// </summary>
public sealed class StepUpStore : IStepUpStore
{
    private readonly IDatabase _db;

    public StepUpStore(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    internal static string GetKey(string userId) => $"stepup:{userId}";

    public async Task SetAsync(string userId, StepUpData data, TimeSpan ttl)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("El ID de usuario no puede estar vacío.", nameof(userId));
        ArgumentNullException.ThrowIfNull(data);
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), "El TTL del step-up debe ser positivo.");

        var json = JsonSerializer.Serialize(data);
        await _db.StringSetAsync(GetKey(userId), json, ttl);
    }

    public async Task<StepUpData?> GetAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

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
    }
}
