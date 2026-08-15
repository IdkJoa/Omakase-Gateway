using System;
using System.Security.Cryptography;
using Application.Common.Security.Mfa;
using Infrastructure.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace OG.Application.UnitTests;

// Pruebas del cifrado en reposo del secreto TOTP (HU-046 / T-102).
public class AesGcmTotpSecretProtectorTests
{
    private static AesGcmTotpSecretProtector CreateSut(string? keyBase64 = null)
    {
        keyBase64 ??= Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return new AesGcmTotpSecretProtector(
            Options.Create(new MfaOptions { TotpEncryptionKey = keyBase64 }));
    }

    [Fact]
    public void ProtectUnprotect_RoundTrips()
    {
        var sut = CreateSut();
        const string secret = "GEZDGNBVGEZDGNBVGEZDGNBVGEZDGNBV";

        var protectedValue = sut.Protect(secret);

        Assert.NotEqual(secret, protectedValue);
        Assert.Equal(secret, sut.Unprotect(protectedValue));
    }

    [Fact]
    public void Protect_SameInputTwice_ProducesDifferentCiphertext()
    {
        // Nonce aleatorio por operación: el cifrado no debe ser determinista.
        var sut = CreateSut();

        Assert.NotEqual(sut.Protect("secreto"), sut.Protect("secreto"));
    }

    [Fact]
    public void Unprotect_TamperedPayload_Throws()
    {
        var sut = CreateSut();
        var payload = Convert.FromBase64String(sut.Protect("secreto"));
        payload[^1] ^= 0xFF; // corromper el último byte del ciphertext

        Assert.ThrowsAny<CryptographicException>(
            () => sut.Unprotect(Convert.ToBase64String(payload)));
    }

    [Fact]
    public void Unprotect_WithDifferentKey_Throws()
    {
        var original = CreateSut();
        var other = CreateSut();

        var protectedValue = original.Protect("secreto");

        Assert.ThrowsAny<CryptographicException>(() => other.Unprotect(protectedValue));
    }

    [Fact]
    public void Constructor_MissingKey_FailsClosed()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new AesGcmTotpSecretProtector(Options.Create(new MfaOptions())));

        Assert.Contains("TotpEncryptionKey", ex.Message);
    }

    [Fact]
    public void Constructor_WrongKeySize_FailsClosed()
    {
        var shortKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        Assert.Throws<InvalidOperationException>(() => CreateSut(shortKey));
    }
}
