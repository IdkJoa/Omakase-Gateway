using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Common.Security;

/// <summary>
/// Servicio unificado de Redis para gestionar el estado efímero del Gateway
/// sin acceso a disco en la ruta crítica de evaluación.
/// </summary>
/// <remarks>
/// El seguimiento de sesión por <c>session:{userId}</c> que contemplaba el SRS §7.11.2 NO existe:
/// el rediseño del cierre de sesión (HU-046) resuelve la revocación con la lista negra de <c>jti</c>
/// más la revocación de refresh tokens por <c>user_id</c> en PostgreSQL, que es autoritativa y
/// sobrevive a un reinicio de Redis. Los métodos de sesión se eliminaron por no tener ningún
/// consumidor: mantenerlos sugería una capacidad de rastreo de sesiones que el sistema no ofrece.
/// </remarks>
public interface IRedisService
{
    #region Lista Negra de Tokens (blacklist:{jti} -> String, TTL: Dinámico)
    /// <summary>
    /// Agrega un JTI a la lista negra con un TTL igual a la expiración restante del token.
    /// </summary>
    Task AddToBlacklistAsync(string jti, TimeSpan expiration);

    /// <summary>
    /// Verifica si un token está revocado (existe en la lista negra).
    /// </summary>
    Task<bool> IsBlacklistedAsync(string jti);
    #endregion

    #region Perfil de Comportamiento IA (profile:{userId} -> String JSON, TTL: 1 hora)
    /// <summary>
    /// Almacena el perfil de comportamiento de IA serializado de un usuario con un TTL de 1 hora.
    /// </summary>
    Task CacheProfileAsync(string userId, string jsonProfile, TimeSpan ttl);

    /// <summary>
    /// Obtiene el perfil de comportamiento de IA en caché para evaluar anomalías sin latencia de disco.
    /// </summary>
    Task<string?> GetCachedProfileAsync(string userId);
    #endregion

    #region Huellas de Navegador (fingerprint:{userId} -> Set, TTL: 24 horas)
    /// <summary>
    /// Añade un hash de huella digital de navegador al conjunto de dispositivos conocidos del usuario.
    /// Renueva el TTL de expiración de 24 horas sobre el Set.
    /// </summary>
    Task StoreFingerprintAsync(string userId, string fingerprintHash, TimeSpan ttl);

    /// <summary>
    /// Obtiene la lista completa de huellas de navegador registradas para el usuario.
    /// </summary>
    Task<List<string>> GetFingerprintsAsync(string userId);
    #endregion

    #region Rate Limiting (ratelimit:{ip} -> String incrementable, TTL: 60s)
    /// <summary>
    /// Incrementa atómicamente el contador de peticiones de una IP y establece un TTL de 60 segundos si es nueva.
    /// </summary>
    Task<long> IncrementRateLimitAsync(string ip, TimeSpan window);
    #endregion

    #region Pub/Sub
    /// <summary>
    /// Publica un mensaje en un canal de Redis (fuego y olvido).
    /// </summary>
    Task PublishAsync(string channel, string message);

    /// <summary>
    /// Se suscribe a un canal de Redis y ejecuta la acción al recibir un mensaje.
    /// </summary>
    Task SubscribeAsync(string channel, Action<string, string> handler);
    #endregion
}
