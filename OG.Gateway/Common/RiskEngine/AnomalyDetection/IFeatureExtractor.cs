namespace Application.Common.RiskEngine.AnomalyDetection;

// Puro y determinista, sin acceso a datos, para reutilizarse tanto en inferencia como en entrenamiento.
public interface IFeatureExtractor
{
    // recentAccesses es historial de contexto y no incluye la petición actual.
    AnomalyFeatureVector Extract(RequestContext context, IReadOnlyList<UserAccessSample> recentAccesses);
}
