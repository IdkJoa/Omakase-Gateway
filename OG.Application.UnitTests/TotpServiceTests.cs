using System;
using Application.Common.Security.Mfa;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas del servicio TOTP (HU-046 / T-102, T-104) contra los vectores de prueba
/// del Apéndice B de RFC 6238 (HMAC-SHA1, semilla ASCII "12345678901234567890").
/// Los códigos de 6 dígitos son los 8 dígitos del RFC módulo 10^6.
/// </summary>
public class TotpServiceTests
{
    // Base32 de "12345678901234567890" (RFC 6238 Apéndice B, SHA1).
    private const string Rfc6238Seed = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    private readonly TotpService _sut = new();

    [Theory]
    [InlineData(59L, "287082")]           // RFC: 94287082
    [InlineData(1111111109L, "081804")]   // RFC: 07081804
    [InlineData(1111111111L, "050471")]   // RFC: 14050471
    [InlineData(1234567890L, "005924")]   // RFC: 89005924
    [InlineData(2000000000L, "279037")]   // RFC: 69279037
    public void ValidateCode_Rfc6238Vectors_AreAccepted(long unixSeconds, string expectedOtp)
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        Assert.True(_sut.ValidateCode(Rfc6238Seed, expectedOtp, at, windowSteps: 0));
    }

    [Fact]
    public void ValidateCode_WithinOneStepWindow_IsAccepted()
    {
        // Código del paso T=59 (287082) presentado 30 s después: dentro de ±1 paso (SRS §3.6).
        var later = DateTimeOffset.FromUnixTimeSeconds(59 + 30);

        Assert.True(_sut.ValidateCode(Rfc6238Seed, "287082", later, windowSteps: 1));
    }

    [Fact]
    public void ValidateCode_OutsideWindow_IsRejected()
    {
        // El mismo código presentado 90 s después queda fuera de la ventana de ±1 paso.
        var tooLate = DateTimeOffset.FromUnixTimeSeconds(59 + 90);

        Assert.False(_sut.ValidateCode(Rfc6238Seed, "287082", tooLate, windowSteps: 1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]      // longitud incorrecta
    [InlineData("1234567")]    // longitud incorrecta
    [InlineData("12a456")]     // no numérico
    public void ValidateCode_MalformedOtp_IsRejected(string otp)
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(59);

        Assert.False(_sut.ValidateCode(Rfc6238Seed, otp, at));
    }

    [Fact]
    public void ValidateCode_InvalidBase32Secret_IsRejected()
    {
        Assert.False(_sut.ValidateCode("not-base32-!!", "287082", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void GenerateSecret_Produces160BitBase32Secret()
    {
        var secret = _sut.GenerateSecret();

        // 20 bytes → 32 caracteres Base32 sin relleno.
        Assert.Equal(32, secret.Length);
        Assert.All(secret, c => Assert.Contains(c, "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"));

        // Debe ser utilizable de inmediato: un secreto inválido nunca validaría formato.
        Assert.NotEqual(_sut.GenerateSecret(), secret); // aleatorio
    }

    [Fact]
    public void BuildProvisioningUri_FollowsOtpauthScheme()
    {
        var uri = _sut.BuildProvisioningUri("Omakase-Gateway", "cliente1", Rfc6238Seed);

        Assert.StartsWith("otpauth://totp/Omakase-Gateway%3Acliente1?", uri);
        Assert.Contains($"secret={Rfc6238Seed}", uri);
        Assert.Contains("issuer=Omakase-Gateway", uri);
        Assert.Contains("algorithm=SHA1", uri);
        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
    }
}
