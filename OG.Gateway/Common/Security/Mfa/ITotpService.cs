namespace Application.Common.Security.Mfa;

/// <summary>
/// Servicio TOTP (RFC 6238 sobre HOTP/RFC 4226) para el step-up MFA de client users
/// (HU-046 / T-102, T-104). Implementación nativa sin dependencias externas.
/// </summary>
public interface ITotpService
{
    /// <summary>Genera un secreto aleatorio de 160 bits codificado en Base32 (RFC 4648).</summary>
    string GenerateSecret();

    /// <summary>
    /// Valida un código de seis dígitos contra el secreto, aceptando una ventana de
    /// ±<paramref name="windowSteps"/> pasos de 30 s (SRS §3.6: ±1 paso).
    /// </summary>
    bool ValidateCode(string base32Secret, string otp, DateTimeOffset utcNow, int windowSteps = 1);

    /// <summary>
    /// Construye el provisioning URI <c>otpauth://totp/...</c> que la app
    /// autenticadora consume (vía QR en HU-047).
    /// </summary>
    string BuildProvisioningUri(string issuer, string username, string base32Secret);
}
