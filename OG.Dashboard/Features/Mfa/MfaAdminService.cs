using System.Security.Cryptography;
using Application.Common.Security.Mfa;
using Domain.Entities;
using Microsoft.Extensions.Options;

namespace OG.Dashboard.Features.Mfa;

/// <remarks>
/// A diferencia del enrolamiento self-service del Gateway (usuario resuelto por su propio JWT),
/// aquí el actor es un administrador y el sujeto llega por la ruta. El servicio MUTA la entidad
/// <see cref="User"/> que recibe pero NO persiste: la escritura (<c>SaveChanges</c>) y las
/// respuestas HTTP son responsabilidad del endpoint.
/// </remarks>
public sealed class MfaAdminService
{
    private readonly ITotpService _totp;
    private readonly ITotpSecretProtector _protector;
    private readonly MfaOptions _options;

    public MfaAdminService(ITotpService totp, ITotpSecretProtector protector, IOptions<MfaOptions> options)
    {
        _totp = totp;
        _protector = protector;
        _options = options.Value;
    }

    /// <summary>
    /// NO es idempotente: cada llamada genera un secreto DISTINTO y descarta el pendiente anterior.
    /// Llamar dos veces invalida el QR de la primera, y el OTP derivado de aquel fallará al confirmar.
    /// </summary>
    public string BeginEnrollment(User user)
    {
        var secret = _totp.GenerateSecret();
        user.TotpSecret = _protector.Protect(secret);
        user.MfaEnabled = false;                 // se activa al confirmar con el primer OTP válido
        user.UpdatedAt = DateTimeOffset.UtcNow;

        return _totp.BuildProvisioningUri(_options.Issuer, user.Username, secret);
    }

    /// <summary>Devuelve <c>false</c> sin mutar el estado si no hay secreto, si es ilegible o si el OTP no valida (fail-closed, SRS §9.4).</summary>
    public bool ConfirmEnrollment(User user, string otp, DateTimeOffset utcNow)
    {
        if (user.TotpSecret is null || string.IsNullOrWhiteSpace(otp))
            return false;

        var secret = TryUnprotect(user.TotpSecret);
        if (secret is null || !_totp.ValidateCode(secret, otp, utcNow))
            return false;

        user.MfaEnabled = true;
        user.UpdatedAt = utcNow;
        return true;
    }

    /// <summary>No toca el lockout de la cuenta: es un concern aparte del bloqueo por intentos.</summary>
    public void Reset(User user)
    {
        user.TotpSecret = null;
        user.MfaEnabled = false;
        user.UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Devuelve <c>null</c> en vez de propagar la excepción criptográfica, para que el consumidor deniegue en vez de exponer un 500.</summary>
    private string? TryUnprotect(string ciphertext)
    {
        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
