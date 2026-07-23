using System;
using System.Security.Cryptography;
using System.Text;
using Application.Common.Security.Mfa;
using Microsoft.Extensions.Options;

namespace Infrastructure.Security;

/// <summary>
/// Cifrado en reposo del secreto TOTP con AES-256-GCM (HU-046 / T-102, SRS §9.9).
/// Formato almacenado: base64(nonce[12] | tag[16] | ciphertext).
/// <para>
/// La clave llega por configuración (<c>Mfa:TotpEncryptionKey</c>): en desarrollo desde
/// configuración local; en producción DEBE inyectarla el proveedor de configuración de
/// Azure Key Vault (SRS §6.3.1) — esta clase no cambia (seam O/C sobre el origen).
/// </para>
/// </summary>
public sealed class AesGcmTotpSecretProtector : ITotpSecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public AesGcmTotpSecretProtector(IOptions<MfaOptions> options)
    {
        var keyBase64 = options.Value.TotpEncryptionKey;
        if (string.IsNullOrWhiteSpace(keyBase64))
            throw new InvalidOperationException(
                "Mfa:TotpEncryptionKey no está configurada. Sin clave de cifrado no se puede " +
                "proteger el secreto TOTP (fail-closed, SRS §6.3.1).");

        _key = Convert.FromBase64String(keyBase64);
        if (_key.Length != 32)
            throw new InvalidOperationException(
                "Mfa:TotpEncryptionKey debe ser una clave AES-256 de 32 bytes en base64.");
    }

    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var payload = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, NonceSize);
        cipher.CopyTo(payload, NonceSize + TagSize);

        CryptographicOperations.ZeroMemory(plainBytes);
        return Convert.ToBase64String(payload);
    }

    public string Unprotect(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrEmpty(ciphertext);

        var payload = Convert.FromBase64String(ciphertext);
        if (payload.Length < NonceSize + TagSize)
            throw new CryptographicException("Payload de secreto TOTP inválido.");

        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipher = payload.AsSpan(NonceSize + TagSize);

        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);

        var result = Encoding.UTF8.GetString(plain);
        CryptographicOperations.ZeroMemory(plain);
        return result;
    }
}
