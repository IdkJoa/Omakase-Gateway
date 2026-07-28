using System.Security.Cryptography;
using Application.Common.Security.Mfa;
using Domain.Entities;
using Microsoft.Extensions.Options;

namespace OG.Dashboard.Features.Mfa;

/// <summary>
/// Lógica de administración de MFA (TOTP) del Dashboard (HU-047 / T-108): enrolamiento iniciado
/// por un administrador sobre un client user objetivo, confirmación del primer OTP y reset del
/// segundo factor. Reutiliza los primitivos de HU-046 (<see cref="ITotpService"/>,
/// <see cref="ITotpSecretProtector"/>) — no reimplementa criptografía.
/// <para>
/// A diferencia del enrolamiento self-service del Gateway (usuario resuelto por su propio JWT),
/// aquí el actor es un administrador y el sujeto llega por la ruta. El servicio MUTA la entidad
/// <see cref="User"/> que recibe pero NO persiste: la escritura (<c>SaveChanges</c>) y las
/// respuestas HTTP son responsabilidad del endpoint (SRP). Sin estado mutable → registrable como Scoped.
/// </para>
/// </summary>
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
    /// Inicia el enrolamiento: genera un secreto TOTP nuevo, lo cifra en reposo sobre la entidad
    /// (<c>MfaEnabled = false</c> hasta confirmar) y devuelve el provisioning URI que la app
    /// autenticadora consume vía QR (T-109). El secreto en claro no se persiste ni se expone
    /// fuera del URI. Reemplaza cualquier secreto pendiente anterior (re-enrolar es idempotente).
    /// </summary>
    public string BeginEnrollment(User user)
    {
        var secret = _totp.GenerateSecret();
        user.TotpSecret = _protector.Protect(secret);
        user.MfaEnabled = false;                 // se activa al confirmar con el primer OTP válido
        user.UpdatedAt = DateTimeOffset.UtcNow;

        return _totp.BuildProvisioningUri(_options.Issuer, user.Username, secret);
    }

    /// <summary>
    /// Confirma el enrolamiento validando el primer OTP contra el secreto pendiente (ventana ±1 paso).
    /// Al éxito activa <c>MfaEnabled</c>. Devuelve <c>false</c> —sin mutar el estado— si no hay
    /// secreto, si el texto cifrado es ilegible (clave rotada/corrupto) o si el OTP no valida
    /// (fail-closed, SRS §9.4).
    /// </summary>
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

    /// <summary>
    /// Resetea el segundo factor: elimina el secreto TOTP y desactiva <c>MfaEnabled</c>; el usuario
    /// deberá re-enrolar. No toca el lockout de la cuenta (<c>LockedUntil</c>/<c>FailedAttempts</c>):
    /// es un concern aparte del bloqueo por intentos (HU-046).
    /// </summary>
    public void Reset(User user)
    {
        user.TotpSecret = null;
        user.MfaEnabled = false;
        user.UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Descifra el secreto tolerando un texto cifrado ilegible (clave rotada, dato corrupto o
    /// migrado con otra clave): devuelve <c>null</c> en vez de propagar la excepción criptográfica,
    /// para que el consumidor deniegue en vez de exponer un 500.
    /// </summary>
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
