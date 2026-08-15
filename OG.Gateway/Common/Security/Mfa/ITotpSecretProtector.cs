namespace Application.Common.Security.Mfa;

// Abstrae el origen de la clave: en producción se deriva de Azure Key Vault; en desarrollo, de
// configuración local. Los consumidores no cambian.
public interface ITotpSecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string ciphertext);
}
