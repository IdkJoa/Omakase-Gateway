using System;
using System.Text.Json;
using System.Threading.Tasks;
using Application.Common.Security.Mfa;
using StackExchange.Redis;

namespace Infrastructure.Redis;

/// <summary>
/// Implementación Redis de <see cref="IChallengeStore"/> (HU-046 / T-103).
/// Clave <c>challenge:{challengeId}</c> → String JSON con TTL 2–5 min (SRS §7.11.2).
/// </summary>
public sealed class ChallengeStore : IChallengeStore
{
    private readonly IDatabase _db;

    public ChallengeStore(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    internal static string GetKey(Guid challengeId) => $"challenge:{challengeId:D}";

    public async Task StoreAsync(Guid challengeId, ChallengeData data, TimeSpan ttl)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), "El TTL del desafío debe ser positivo.");

        var json = JsonSerializer.Serialize(data);
        await _db.StringSetAsync(GetKey(challengeId), json, ttl);
    }

    public async Task<ChallengeData?> GetAsync(Guid challengeId)
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
    }

    public async Task RemoveAsync(Guid challengeId)
    {
        await _db.KeyDeleteAsync(GetKey(challengeId));
    }
}
