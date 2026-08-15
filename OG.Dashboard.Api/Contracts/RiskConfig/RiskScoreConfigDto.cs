namespace OG.Dashboard.Api.Contracts.RiskConfig;

/// <summary>Row única en BD. Fórmula: RiskScore = Wp*PolicyScore + Wa*AnomalyScore + ColdStartPenalty; PolicyWeight + AnomalyWeight debe ser 1.</summary>
public sealed record RiskScoreConfigDto(
    Guid Id,
    decimal PolicyWeight,
    decimal AnomalyWeight,

    /// <summary>Se reduce linealmente con cada acceso hasta extinguirse en ColdStartN accesos.</summary>
    decimal ColdStartPenalty,
    int ColdStartN,

    /// <summary>Por encima de este umbral el veredicto es BLOCK (SRS §7.6 challenge_threshold).</summary>
    decimal BlockThreshold,

    /// <summary>Por encima de este umbral el veredicto es CHALLENGE; debe ser menor que BlockThreshold (SRS §7.6 allow_threshold).</summary>
    decimal ChallengeThreshold,
    DateTimeOffset UpdatedAt
);
