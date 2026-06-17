using System.ComponentModel.DataAnnotations;

namespace OG.Dashboard.Api.Contracts.RiskConfig;

/// <summary>
/// Payload para actualizar la configuración del motor de riesgo.
/// Usado en PUT /api/v1/risk-config.
/// Todos los campos son requeridos para garantizar un estado de configuración coherente.
/// </summary>
public sealed record UpdateRiskConfigRequest(
    /// <summary>
    /// Peso del PolicyScore (capa determinista). Debe estar en [0, 1].
    /// PolicyWeight + AnomalyWeight debe ser igual a 1.
    /// </summary>
    [Range(0.0, 1.0)]
    decimal PolicyWeight,

    /// <summary>
    /// Peso del AnomalyScore (capa IA). Debe estar en [0, 1].
    /// </summary>
    [Range(0.0, 1.0)]
    decimal AnomalyWeight,

    /// <summary>Penalización base de cold-start en puntos (0–100).</summary>
    [Range(0.0, 100.0)]
    decimal ColdStartPenalty,

    /// <summary>Número de accesos N a partir del cual se elimina la penalización cold-start.</summary>
    [Range(1, 1000)]
    int ColdStartN,

    /// <summary>Umbral de bloqueo. Debe ser mayor que ChallengeThreshold.</summary>
    [Range(0.0, 100.0)]
    decimal BlockThreshold,

    /// <summary>Umbral de desafío. Debe ser menor que BlockThreshold.</summary>
    [Range(0.0, 100.0)]
    decimal ChallengeThreshold
);
