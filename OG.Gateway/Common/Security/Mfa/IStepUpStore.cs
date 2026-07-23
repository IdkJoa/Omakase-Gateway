namespace Application.Common.Security.Mfa;

/// <summary>
/// Ventana de step-up satisfecho, persistida como <c>stepup:{userId}</c>
/// (TTL 10 min, ligada a la huella del dispositivo — SRS §3.6).
/// </summary>
/// <param name="FingerprintHash">Huella del dispositivo que completó el desafío; null si no se pudo calcular.</param>
/// <param name="GrantedAt">Instante en que se satisfizo el step-up.</param>
public sealed record StepUpData(string? FingerprintHash, DateTimeOffset GrantedAt);

/// <summary>
/// Almacén de la ventana de step-up (HU-046 / T-104, T-105). Dentro de la ventana,
/// el consolidador degrada Challenge→Allow para el mismo usuario y dispositivo.
/// </summary>
public interface IStepUpStore
{
    /// <summary>Fija la ventana de step-up del usuario con su TTL.</summary>
    Task SetAsync(string userId, StepUpData data, TimeSpan ttl);

    /// <summary>Obtiene la ventana vigente; null si no existe o expiró.</summary>
    Task<StepUpData?> GetAsync(string userId);
}
