namespace Application.Common.Security.Mfa;

/// <summary>
/// Contexto mínimo de la petición original que queda pendiente de step-up,
/// persistido en Redis como <c>challenge:{challengeId}</c> (HU-046 / T-103).
/// </summary>
/// <param name="UserId">Actor autenticado que recibió el veredicto Challenge.</param>
/// <param name="ServiceName">Servicio destino de la petición original (puede ser null).</param>
/// <param name="FingerprintHash">Huella del dispositivo que originó el desafío; liga el step-up al dispositivo.</param>
/// <param name="SourceIp">IP de origen de la petición original.</param>
/// <param name="CreatedAt">Instante de emisión del desafío.</param>
public sealed record ChallengeData(
    string UserId,
    string? ServiceName,
    string? FingerprintHash,
    string SourceIp,
    DateTimeOffset CreatedAt);

/// <summary>
/// Almacén efímero de desafíos MFA pendientes (<c>challenge:{challengeId}</c>,
/// TTL 2–5 min — SRS §7.11.2). El desafío es de uso único: se elimina al verificarse.
/// </summary>
public interface IChallengeStore
{
    /// <summary>Persiste el desafío con su TTL.</summary>
    Task StoreAsync(Guid challengeId, ChallengeData data, TimeSpan ttl);

    /// <summary>Recupera el desafío vigente; null si no existe o expiró.</summary>
    Task<ChallengeData?> GetAsync(Guid challengeId);

    /// <summary>Elimina el desafío (uso único tras verificación exitosa).</summary>
    Task RemoveAsync(Guid challengeId);
}
