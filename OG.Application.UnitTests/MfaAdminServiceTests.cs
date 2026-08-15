using System;
using Application.Common.Security.Mfa;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure.Security;
using Microsoft.Extensions.Options;
using OG.Dashboard.Features.Mfa;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas del servicio de administración de MFA del Dashboard (HU-047 / T-108). Usa los servicios
/// REALES de HU-046 (<see cref="TotpService"/> + <see cref="AesGcmTotpSecretProtector"/>), sin mocks:
/// lo que se prueba es el enrolamiento/confirmación/reset sobre la entidad <see cref="User"/> y el
/// comportamiento fail-closed. La confirmación con OTP válido usa el vector RFC 6238 (Apéndice B).
/// </summary>
public class MfaAdminServiceTests
{
    // Clave AES-256 dev (32 bytes en base64) — mismo marcador reproducible que appsettings dev.
    private const string DevKey = "T01BS0FTRS1ERVYtT05MWS1UT1RQLUtFWS0zMkJZVEU=";

    // Vector RFC 6238 (Apéndice B, SHA1): semilla en Base32 y su OTP en T=59 s.
    private const string Rfc6238Seed = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
    private const string Rfc6238OtpAt59 = "287082";
    private static readonly DateTimeOffset At59 = DateTimeOffset.FromUnixTimeSeconds(59);

    private readonly AesGcmTotpSecretProtector _protector =
        new(Options.Create(new MfaOptions { TotpEncryptionKey = DevKey }));

    private MfaAdminService CreateSut() =>
        new(new TotpService(), _protector,
            Options.Create(new MfaOptions { TotpEncryptionKey = DevKey, Issuer = "Omakase-Test" }));

    private static User NewClientUser(string username = "cliente.test") => new()
    {
        Id = UserId.From(Guid.NewGuid()),
        Username = username,
        Type = UserType.Client,
        PasswordHash = "hash",
        KeycloakSub = Guid.NewGuid().ToString(),
    };

    [Fact]
    public void BeginEnrollment_StoresEncryptedSecret_AndReturnsProvisioningUri()
    {
        var sut = CreateSut();
        var user = NewClientUser("cliente1");

        var uri = sut.BeginEnrollment(user);

        // Lo persistido está CIFRADO y es descifrable a un secreto Base32 usable de inmediato.
        Assert.NotNull(user.TotpSecret);
        var plaintext = _protector.Unprotect(user.TotpSecret!);
        Assert.Equal(32, plaintext.Length);            // 160 bits en Base32
        Assert.NotEqual(plaintext, user.TotpSecret);   // lo almacenado NO es el secreto en claro

        // Todavía no activo: se activa solo al confirmar el primer OTP.
        Assert.False(user.MfaEnabled);

        // El provisioning URI (para el QR de T-109) referencia issuer, usuario y el secreto en claro.
        Assert.StartsWith("otpauth://totp/Omakase-Test%3Acliente1?", uri);
        Assert.Contains($"secret={plaintext}", uri);
        Assert.Contains("issuer=Omakase-Test", uri);
    }

    [Fact]
    public void ConfirmEnrollment_WithValidOtp_ActivatesMfa()
    {
        var sut = CreateSut();
        var user = NewClientUser();
        user.TotpSecret = _protector.Protect(Rfc6238Seed);   // secreto pendiente conocido (vector RFC)

        var ok = sut.ConfirmEnrollment(user, Rfc6238OtpAt59, At59);

        Assert.True(ok);
        Assert.True(user.MfaEnabled);
    }

    [Fact]
    public void ConfirmEnrollment_WithWrongOtp_DoesNotActivate()
    {
        var sut = CreateSut();
        var user = NewClientUser();
        user.TotpSecret = _protector.Protect(Rfc6238Seed);

        var ok = sut.ConfirmEnrollment(user, "000000", At59);

        Assert.False(ok);
        Assert.False(user.MfaEnabled);
    }

    [Fact]
    public void ConfirmEnrollment_WithoutPendingSecret_ReturnsFalse()
    {
        var sut = CreateSut();
        var user = NewClientUser();

        Assert.False(sut.ConfirmEnrollment(user, Rfc6238OtpAt59, At59));
        Assert.False(user.MfaEnabled);
    }

    [Fact]
    public void ConfirmEnrollment_WithUnreadableSecret_FailsClosed()
    {
        var sut = CreateSut();
        var user = NewClientUser();
        user.TotpSecret = "not-a-valid-ciphertext";   // ilegible → se deniega, no revienta

        Assert.False(sut.ConfirmEnrollment(user, Rfc6238OtpAt59, At59));
        Assert.False(user.MfaEnabled);
    }

    [Fact]
    public void Reset_ClearsSecretAndDisablesMfa()
    {
        var sut = CreateSut();
        var user = NewClientUser();
        user.TotpSecret = _protector.Protect(Rfc6238Seed);
        user.MfaEnabled = true;

        sut.Reset(user);

        Assert.Null(user.TotpSecret);
        Assert.False(user.MfaEnabled);
    }
}
