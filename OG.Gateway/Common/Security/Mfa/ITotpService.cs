namespace Application.Common.Security.Mfa;

// RFC 6238 (TOTP) sobre RFC 4226 (HOTP). Implementación nativa sin dependencias externas.
public interface ITotpService
{
    string GenerateSecret();

    // windowSteps acepta un margen de pasos de 30s para tolerar desfase de reloj del cliente.
    bool ValidateCode(string base32Secret, string otp, DateTimeOffset utcNow, int windowSteps = 1);

    string BuildProvisioningUri(string issuer, string username, string base32Secret);
}
