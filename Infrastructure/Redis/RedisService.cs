using Application.Common.Security;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Infrastructure.Redis;

/// <summary>
/// Implementación unificada del servicio de Redis utilizando StackExchange.Redis.
/// </summary>
public sealed class RedisService : IRedisService
{
    private readonly IDatabase _db;

    public RedisService(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    

    #region Lista Negra de Tokens (blacklist:{jti} -> String, TTL: Dinámico)
    public async Task AddToBlacklistAsync(string jti, TimeSpan expiration)
    {
        if (string.IsNullOrWhiteSpace(jti))
            throw new ArgumentException("El JTI del token no puede estar vacío.", nameof(jti));

        if (expiration <= TimeSpan.Zero)
            return;

        string key = RedisKeyHelper.GetBlacklistKey(jti);
        await _db.StringSetAsync(key, "revoked", expiration);
    }

    public async Task<bool> IsBlacklistedAsync(string jti)
    {
        if (string.IsNullOrWhiteSpace(jti))
            return false;

        string key = RedisKeyHelper.GetBlacklistKey(jti);
        return await _db.KeyExistsAsync(key);
    }
    #endregion

    #region Perfil de Comportamiento IA (profile:{userId} -> String JSON, TTL: 1 hora)
    public async Task CacheProfileAsync(string userId, string jsonProfile, TimeSpan ttl)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("El ID de usuario no puede estar vacío.", nameof(userId));

        string key = RedisKeyHelper.GetProfileKey(userId);
        await _db.StringSetAsync(key, jsonProfile, ttl);
    }

    public async Task<string?> GetCachedProfileAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        string key = RedisKeyHelper.GetProfileKey(userId);
        return await _db.StringGetAsync(key);
    }
    #endregion

    #region Huellas de Navegador (fingerprint:{userId} -> Set, TTL: 24 horas)
    public async Task StoreFingerprintAsync(string userId, string fingerprintHash, TimeSpan ttl)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("El ID de usuario no puede estar vacío.", nameof(userId));
        if (string.IsNullOrWhiteSpace(fingerprintHash))
            throw new ArgumentException("La huella digital no puede estar vacía.", nameof(fingerprintHash));

        string key = RedisKeyHelper.GetFingerprintKey(userId);
        await _db.SetAddAsync(key, fingerprintHash);
        await _db.KeyExpireAsync(key, ttl);
    }

    public async Task<List<string>> GetFingerprintsAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return new List<string>();

        string key = RedisKeyHelper.GetFingerprintKey(userId);
        var members = await _db.SetMembersAsync(key);
        
        return members.Select(m => m.ToString()).ToList();
    }
    #endregion

    #region Rate Limiting (ratelimit:{ip} -> String incrementable, TTL: 60s)
    public async Task<long> IncrementRateLimitAsync(string ip, TimeSpan window)
    {
        if (string.IsNullOrWhiteSpace(ip))
            throw new ArgumentException("La dirección IP no puede estar vacía.", nameof(ip));

        string key = RedisKeyHelper.GetRateLimitKey(ip);
        long count = await _db.StringIncrementAsync(key);

        if (count == 1)
        {
            await _db.KeyExpireAsync(key, window);
        }

        return count;
    }

    public async Task<long> GetRateLimitAsync(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return 0;

        string key = RedisKeyHelper.GetRateLimitKey(ip);
        var value = await _db.StringGetAsync(key);

        if (value.HasValue && long.TryParse(value.ToString(), out var count))
        {
            return count;
        }

        return 0;
    }
    #endregion
}
