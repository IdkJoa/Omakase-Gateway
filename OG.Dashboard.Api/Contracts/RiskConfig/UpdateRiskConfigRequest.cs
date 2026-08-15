using System.ComponentModel.DataAnnotations;

namespace OG.Dashboard.Api.Contracts.RiskConfig;

/// <summary>Todos los campos son requeridos para garantizar un estado de configuración coherente. PolicyWeight + AnomalyWeight debe ser igual a 1; BlockThreshold debe ser mayor que ChallengeThreshold.</summary>
public sealed record UpdateRiskConfigRequest(
    [Range(0.0, 1.0)]
    decimal PolicyWeight,

    [Range(0.0, 1.0)]
    decimal AnomalyWeight,

    [Range(0.0, 100.0)]
    decimal ColdStartPenalty,

    [Range(1, 1000)]
    int ColdStartN,

    [Range(0.0, 100.0)]
    decimal BlockThreshold,

    [Range(0.0, 100.0)]
    decimal ChallengeThreshold
);
