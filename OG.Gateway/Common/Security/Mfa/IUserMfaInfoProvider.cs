namespace Application.Common.Security.Mfa;

/// <summary>
/// Proyección mínima del estado MFA de un usuario para la ruta de evaluación.
/// </summary>
/// <param name="IsInteractive">False para service accounts: Challenge escala a Block (T-106).</param>
/// <param name="MfaEnabled">True si la cuenta tiene el step-up TOTP activo.</param>
public sealed record UserMfaInfo(bool IsInteractive, bool MfaEnabled);

/// <summary>
/// Puerto de datos del motor de riesgo para consultar el estado MFA del actor
/// (HU-046 / T-105, T-106). Implementado en Infrastructure sobre EF Core.
/// </summary>
public interface IUserMfaInfoProvider
{
    /// <summary>Devuelve el estado MFA del usuario; null si el id no es válido o no existe.</summary>
    Task<UserMfaInfo?> GetAsync(string userId, CancellationToken cancellationToken = default);
}
