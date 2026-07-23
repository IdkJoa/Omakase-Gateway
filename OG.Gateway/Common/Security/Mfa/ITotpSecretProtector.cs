namespace Application.Common.Security.Mfa;

/// <summary>
/// Cifrado en reposo del secreto TOTP (HU-046 / T-102, SRS §9.9).
/// Abstrae el origen de la clave: en producción se deriva de Azure Key Vault
/// (SRS §6.3.1); en desarrollo, de configuración local. Los consumidores no cambian.
/// </summary>
public interface ITotpSecretProtector
{
    /// <summary>Cifra el secreto en claro y devuelve el texto cifrado serializado (base64).</summary>
    string Protect(string plaintext);

    /// <summary>Descifra el valor almacenado en <c>users.totp_secret</c>.</summary>
    string Unprotect(string ciphertext);
}
