namespace OG.Dashboard.Api.Contracts.RiskConfig;

/// <summary>
/// Representa la configuración global del motor de evaluación de riesgo.
/// Fórmula: RiskScore = Wp*PolicyScore + Wa*AnomalyScore + ColdStartPenalty
/// Corresponde a la entidad <c>RiskScoreConfig</c> del dominio (row única en BD).
/// </summary>
public sealed record RiskScoreConfigDto(
    Guid Id,

    /// <summary>
    /// Peso (0–1) aplicado al PolicyScore en el cálculo final.
    /// PolicyWeight + AnomalyWeight debe ser igual a 1.
    /// Default: 0.6
    /// </summary>
    decimal PolicyWeight,

    /// <summary>
    /// Peso (0–1) aplicado al AnomalyScore (capa IA) en el cálculo final.
    /// Default: 0.4
    /// </summary>
    decimal AnomalyWeight,

    /// <summary>
    /// Penalización base de cold-start aplicada a usuarios sin historial suficiente.
    /// Se reduce linealmente con cada acceso hasta extinguirse en N accesos.
    /// Default: 30 puntos.
    /// </summary>
    decimal ColdStartPenalty,

    /// <summary>
    /// Umbral N de accesos a partir del cual la penalización cold-start se anula.
    /// Default: 10 accesos.
    /// </summary>
    int ColdStartN,

    /// <summary>Risk Score por encima del cual el veredicto es BLOCK. Default: 75.</summary>
    decimal BlockThreshold,

    /// <summary>Risk Score por encima del cual el veredicto es CHALLENGE. Default: 50.</summary>
    decimal ChallengeThreshold,

    /// <summary>Timestamp de la última modificación de la configuración.</summary>
    DateTimeOffset UpdatedAt
);
