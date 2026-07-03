namespace Application.Common.RiskEngine.AnomalyDetection;

/// <summary>
/// Vista del perfil de comportamiento de un usuario que necesita el motor de anomalías en
/// inferencia (HU-016 / T-033). Se hidrata desde Redis (caliente) o PostgreSQL (frío), pero el
/// motor no conoce esa procedencia — depende solo de este contrato.
/// </summary>
/// <param name="TrainingWindow">
/// Ventana rodante de vectores de características históricos con la que se (re)entrena el modelo
/// RandomizedPCA del usuario. Persistida en el <c>feature_vector</c> (JSONB) de <c>user_behavior_profiles</c>.
/// </param>
/// <param name="RecentAccesses">
/// Accesos recientes (instante + endpoint) usados para derivar la frecuencia y diversidad de la
/// petición ACTUAL vía <see cref="IFeatureExtractor"/>.
/// </param>
/// <param name="AccessCount">Total de accesos evaluados acumulados (gobierna cold-start, HU-017).</param>
/// <param name="IsColdStart">True mientras <see cref="AccessCount"/> no alcanza el umbral N.</param>
public sealed record UserAnomalyProfile(
    IReadOnlyList<AnomalyFeatureVector> TrainingWindow,
    IReadOnlyList<UserAccessSample> RecentAccesses,
    int AccessCount,
    bool IsColdStart);
