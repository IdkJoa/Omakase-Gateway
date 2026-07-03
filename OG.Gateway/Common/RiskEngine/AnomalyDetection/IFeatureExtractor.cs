namespace Application.Common.RiskEngine.AnomalyDetection;

/// <summary>
/// Transforma una petición (más la ventana de accesos recientes del usuario) en el
/// <see cref="AnomalyFeatureVector"/> normalizado que alimenta al modelo (HU-016 / T-031).
/// <para>Costura (seam) del motor de anomalías: es puro y determinista, sin acceso a datos,
/// para poder probarlo en aislamiento y reutilizarlo tanto en inferencia como en entrenamiento.</para>
/// </summary>
public interface IFeatureExtractor
{
    /// <summary>
    /// Extrae el vector de características para <paramref name="context"/> usando
    /// <paramref name="recentAccesses"/> como historial de contexto (no incluye la petición actual).
    /// </summary>
    AnomalyFeatureVector Extract(RequestContext context, IReadOnlyList<UserAccessSample> recentAccesses);
}
