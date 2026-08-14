using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Common.Security;

// El seguimiento de sesión por session:{userId} (antes contemplado) ya no existe: el rediseño del
// cierre de sesión resuelve la revocación con la lista negra de jti más la revocación de refresh
// tokens por user_id en PostgreSQL, que es autoritativa y sobrevive a un reinicio de Redis.
public interface IRedisService
{
    #region Lista Negra de Tokens (blacklist:{jti} -> String, TTL: Dinámico)
    Task AddToBlacklistAsync(string jti, TimeSpan expiration);

    Task<bool> IsBlacklistedAsync(string jti);
    #endregion

    #region Perfil de Comportamiento IA (profile:{userId} -> String JSON, TTL: 1 hora)
    Task CacheProfileAsync(string userId, string jsonProfile, TimeSpan ttl);

    Task<string?> GetCachedProfileAsync(string userId);
    #endregion

    #region Huellas de Navegador (fingerprint:{userId} -> Set, TTL: 24 horas)
    Task StoreFingerprintAsync(string userId, string fingerprintHash, TimeSpan ttl);

    Task<List<string>> GetFingerprintsAsync(string userId);
    #endregion

    #region Rate Limiting (ratelimit:{ip} -> String incrementable, TTL: 60s)
    Task<long> IncrementRateLimitAsync(string ip, TimeSpan window);
    #endregion

    #region Pub/Sub
    Task PublishAsync(string channel, string message);

    Task SubscribeAsync(string channel, Action<string, string> handler);
    #endregion
}
