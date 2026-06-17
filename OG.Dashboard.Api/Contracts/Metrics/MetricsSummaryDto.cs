namespace OG.Dashboard.Api.Contracts.Metrics;

/// <summary>
/// Resumen de métricas del Dashboard — GET /api/v1/metrics/summary.
/// Cubre los KPIs del panel principal: totales, porcentajes y serie temporal del Risk Score.
/// </summary>
public sealed record MetricsSummaryDto(
    /// <summary>Total de evaluaciones realizadas en el período seleccionado.</summary>
    long TotalEvaluations,

    /// <summary>Evaluaciones con veredicto ALLOW.</summary>
    long AllowedCount,

    /// <summary>Evaluaciones con veredicto CHALLENGE.</summary>
    long ChallengedCount,

    /// <summary>Evaluaciones con veredicto BLOCK.</summary>
    long BlockedCount,

    /// <summary>Porcentaje de evaluaciones permitidas (0–100).</summary>
    double AllowedPercent,

    /// <summary>Porcentaje de evaluaciones desafiadas (0–100).</summary>
    double ChallengedPercent,

    /// <summary>Porcentaje de evaluaciones bloqueadas (0–100).</summary>
    double BlockedPercent,

    /// <summary>Risk Score promedio en el período.</summary>
    double AverageRiskScore,

    /// <summary>Usuarios únicos evaluados en el período.</summary>
    int UniqueUsers,

    /// <summary>
    /// Serie temporal del Risk Score promedio agrupado por hora o día.
    /// Usada para alimentar el gráfico de tendencia en el Dashboard Angular.
    /// </summary>
    IReadOnlyList<RiskScorePointDto> RiskScoreSeries
);

/// <summary>Punto en la serie temporal del Risk Score promedio.</summary>
public sealed record RiskScorePointDto(
    DateTimeOffset Timestamp,
    double AvgScore,
    long EvaluationCount
);
