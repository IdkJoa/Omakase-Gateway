namespace Application.Common.Options;

/// <summary>
/// Configuración tipada para la emisión del JWT propio del Gateway (HS256).
/// La clave secreta (<see cref="SecretKey"/>) se inyecta desde Azure Key Vault
/// en producción y desde appsettings/user-secrets en desarrollo.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Clave simétrica HS256. Mínimo 32 bytes (256 bits).</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Emisor (<c>iss</c>) incluido en el token.</summary>
    public string Issuer { get; init; } = "omakase-gateway";

    /// <summary>Audiencia (<c>aud</c>) incluida en el token.</summary>
    public string Audience { get; init; } = "omakase-services";

    /// <summary>Tiempo de vida del access token en minutos. Por defecto 15.</summary>
    public int AccessTokenTtlMinutes { get; init; } = 15;

    /// <summary>Tiempo de vida del refresh token en días. Por defecto 7.</summary>
    public int RefreshTokenTtlDays { get; init; } = 7;
}
