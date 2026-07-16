namespace Application.Common.Security.Mfa;

/// <summary>
/// Configuración tipada del flujo de step-up MFA (TOTP) para client users
/// (HU-046 / SRS §3.6 y §9.9). Se enlaza desde la sección <c>Mfa</c> del host.
/// </summary>
public sealed class MfaOptions
{
    public const string SectionName = "Mfa";

    /// <summary>TTL del desafío pendiente <c>challenge:{challengeId}</c> (SRS §7.11.2: 2–5 min).</summary>
    public int ChallengeTtlSeconds { get; set; } = 180;

    /// <summary>TTL de la ventana de step-up satisfecho <c>stepup:{userId}</c> (SRS: 10 min).</summary>
    public int StepUpTtlMinutes { get; set; } = 10;

    /// <summary>Intentos máximos de verificación TOTP por ventana antes de bloquear la cuenta.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Ventana del contador <c>mfaattempts:{userId}</c> en minutos.</summary>
    public int AttemptWindowMinutes { get; set; } = 15;

    /// <summary>Duración del bloqueo de cuenta al superar <see cref="MaxAttempts"/> (SRS: 30 min).</summary>
    public int LockoutMinutes { get; set; } = 30;

    /// <summary>Emisor mostrado en la app autenticadora (provisioning URI).</summary>
    public string Issuer { get; set; } = "Omakase-Gateway";

    /// <summary>
    /// Clave AES-256 (base64, 32 bytes) para cifrar el secreto TOTP en reposo.
    /// En desarrollo proviene de configuración local NO versionada en producción;
    /// en producción DEBE inyectarse desde Azure Key Vault (SRS §6.3.1) vía el
    /// proveedor de configuración — el consumidor (ITotpSecretProtector) no cambia.
    /// </summary>
    public string? TotpEncryptionKey { get; set; }
}
