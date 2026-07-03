namespace Application.Common.RiskEngine.AnomalyDetection;

/// <summary>
/// Mensaje de actualización de perfil encolado tras cada evaluación (HU-016 T-033 / HU-017 T-034).
/// Lleva lo mínimo para que el worker recomponga las features y actualice <c>user_behavior_profiles</c>
/// fuera de la ruta crítica.
/// </summary>
/// <param name="UserId">Identificador del usuario (claim sub / GUID).</param>
/// <param name="Timestamp">Instante de la petición evaluada.</param>
/// <param name="Endpoint">Servicio/endpoint accedido (para la feature de diversidad).</param>
/// <param name="ColdStartN">Umbral N para recalcular <c>is_cold_start</c> tras incrementar el contador.</param>
public sealed record ProfileUpdate(
    string UserId,
    DateTimeOffset Timestamp,
    string Endpoint,
    int ColdStartN);

/// <summary>
/// Canal asíncrono (fire-and-forget) que desacopla la evaluación de la persistencia del perfil,
/// protegiendo el presupuesto de latencia ≤50 ms (T-033). Escritores: el handler del motor de riesgo,
/// uno por petición. Lector único: <c>ProfileUpdateWorker</c>.
/// </summary>
public interface IProfileUpdateChannel
{
    /// <summary>Encola una actualización de forma no-bloqueante; <c>false</c> si el canal está lleno.</summary>
    bool TryWrite(ProfileUpdate update);

    /// <summary>Secuencia asíncrona consumida por el worker de persistencia.</summary>
    IAsyncEnumerable<ProfileUpdate> ReadAllAsync(CancellationToken cancellationToken = default);
}
