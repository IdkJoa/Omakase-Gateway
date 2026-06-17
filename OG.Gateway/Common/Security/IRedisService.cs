using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Common.Security;

/// <summary>
/// DTO que representa la información de sesión activa almacenada en Redis.
/// </summary>
public record SessionData(string Jti, string UserType, DateTimeOffset LastActivity);

/// <summary>
/// Servicio unificado de Redis para gestionar el estado efímero del Gateway
/// sin acceso a disco en la ruta crítica de evaluación.
/// </summary>
public interface IRedisService
{
    #region Sesiones Activas (session:{userId} -> Hash, TTL: 15 min)
    /// <summary>
    /// Almacena los datos de la sesión de un usuario en un Hash de Redis con un TTL de 15 minutos.
    /// </summary>
    Task SetSessionAsync(string userId, string jti, string userType, TimeSpan ttl);

    /// <summary>
    /// Obtiene los datos de sesión activa del usuario. Retorna null si la sesión no existe o expiró.
    /// </summary>
    Task<SessionData?> GetSessionAsync(string userId);

    /// <summary>
    /// Elimina de forma inmediata la sesión de un usuario.
    /// </summary>
    Task InvalidateSessionAsync(string userId);
    #endregion

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

    /// <summary>
    /// Obtiene el contador actual de peticiones para una IP sin incrementarlo.
    /// </summary>
    Task<long> GetRateLimitAsync(string ip);
    #endregion
}
