namespace Infrastructure.Redis;

/// <summary>
/// Proveedor centralizado de nombres de claves para Redis, evitando hardcoding
/// y garantizando la coherencia entre todos los módulos.
/// </summary>
public static class RedisKeyHelper
{
    /// <summary>
    /// Genera la clave de rate limit para una dirección IP específica.
    /// Clave: ratelimit:{ip}
    /// </summary>
    public static string GetRateLimitKey(string ip) => $"ratelimit:{ip}";

    /// <summary>
    /// Genera la clave de lista negra para un token JWT mediante su JTI.
    /// Clave: blacklist:{jti}
    /// </summary>
    public static string GetBlacklistKey(string jti) => $"blacklist:{jti}";

    /// <summary>
    /// Genera la clave para el perfil de comportamiento de IA de un usuario.
    /// Clave: profile:{userId}
    /// </summary>
    public static string GetProfileKey(string userId) => $"profile:{userId}";

    /// <summary>
    /// Genera la clave para el conjunto de huellas del navegador registradas de un usuario.
    /// Clave: fingerprint:{userId}
    /// </summary>
    public static string GetFingerprintKey(string userId) => $"fingerprint:{userId}";
}
