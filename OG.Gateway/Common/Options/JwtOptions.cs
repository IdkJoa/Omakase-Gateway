namespace Application.Common.Options;

// SecretKey se inyecta desde Azure Key Vault en producción y desde appsettings/user-secrets en desarrollo.
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    // Mínimo 32 bytes (256 bits) para HS256.
    public string SecretKey { get; set; } = string.Empty;

    public string Issuer { get; init; } = "omakase-gateway";

    public string Audience { get; init; } = "omakase-services";

    public int AccessTokenTtlMinutes { get; init; } = 15;

    public int RefreshTokenTtlDays { get; init; } = 7;
}
