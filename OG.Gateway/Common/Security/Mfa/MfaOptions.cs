namespace Application.Common.Security.Mfa;

public sealed class MfaOptions
{
    public const string SectionName = "Mfa";

    public int ChallengeTtlSeconds { get; set; } = 180;

    public int StepUpTtlMinutes { get; set; } = 10;

    public int MaxAttempts { get; set; } = 5;

    public int AttemptWindowMinutes { get; set; } = 15;

    public int LockoutMinutes { get; set; } = 30;

    public string Issuer { get; set; } = "Omakase-Gateway";

    // Clave AES-256 (base64, 32 bytes). En producción DEBE inyectarse desde Azure Key Vault, no
    // desde configuración local versionada.
    public string? TotpEncryptionKey { get; set; }
}
