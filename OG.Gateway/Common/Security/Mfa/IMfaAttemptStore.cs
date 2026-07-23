namespace Application.Common.Security.Mfa;

/// <summary>
/// Contador anti fuerza bruta de verificaciones TOTP (<c>mfaattempts:{userId}</c>,
/// SRS §7.11.2). Al superar el máximo por ventana, la cuenta se bloquea (HTTP 423).
/// </summary>
public interface IMfaAttemptStore
{
    /// <summary>Incrementa atómicamente el contador y fija el TTL de la ventana si es nuevo.</summary>
    Task<long> IncrementAsync(string userId, TimeSpan window);

    /// <summary>Reinicia el contador tras una verificación exitosa.</summary>
    Task ResetAsync(string userId);
}
