namespace Application.Common.RiskEngine.AnomalyDetection;

// Lleva lo mínimo para que el worker recomponga las features y actualice user_behavior_profiles
// fuera de la ruta crítica.
public sealed record ProfileUpdate(
    string UserId,
    DateTimeOffset Timestamp,
    string Endpoint,
    decimal BaseRiskPenalty,
    int ColdStartN);

// Canal fire-and-forget que desacopla la evaluación de la persistencia del perfil, para proteger
// el presupuesto de latencia <=50 ms (T-033).
public interface IProfileUpdateChannel
{
    bool TryWrite(ProfileUpdate update);

    IAsyncEnumerable<ProfileUpdate> ReadAllAsync(CancellationToken cancellationToken = default);
}
