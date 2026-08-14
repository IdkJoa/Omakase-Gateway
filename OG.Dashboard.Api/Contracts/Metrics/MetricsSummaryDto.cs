namespace OG.Dashboard.Api.Contracts.Metrics;

public sealed record MetricsSummaryDto(
    long TotalEvaluations,
    long AllowedCount,
    long ChallengedCount,
    long BlockedCount,
    double AllowedPercent,
    double ChallengedPercent,
    double BlockedPercent,
    double AverageRiskScore,
    int UniqueUsers,

    /// <summary>Agrupada por hora o día; alimenta el gráfico de tendencia del Dashboard.</summary>
    IReadOnlyList<RiskScorePointDto> RiskScoreSeries
);

public sealed record RiskScorePointDto(
    DateTimeOffset Timestamp,
    double AvgScore,
    long EvaluationCount
);
