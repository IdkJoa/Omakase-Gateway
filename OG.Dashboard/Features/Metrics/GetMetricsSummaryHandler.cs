using Domain.Common;
using Domain.Entities;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace OG.Dashboard.Features.Metrics;

public sealed record MetricsSummaryResult(
    long TotalEvaluations,
    long AllowedCount,
    long ChallengedCount,
    long BlockedCount,
    double AllowedPercent,
    double ChallengedPercent,
    double BlockedPercent,
    double AverageRiskScore,
    int UniqueUsers,
    IReadOnlyList<RiskScorePointResult> RiskScoreSeries
);

public sealed record RiskScorePointResult(
    DateTimeOffset Timestamp,
    double AvgScore,
    long EvaluationCount
);

/// <summary>
/// Handler de aplicación para calcular métricas y KPIs reales sobre audit_logs (HU-021 / T-061).
/// Soporta filtrado opcional por rango de fechas (por defecto últimas 24 horas).
/// </summary>
public sealed class GetMetricsSummaryHandler(
    OmakaseDbContext context,
    ILogger<GetMetricsSummaryHandler> logger)
{
    public async Task<Result<MetricsSummaryResult>> GetMetricsSummaryAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var toDate = (to ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var fromDate = (from ?? toDate.AddHours(-24)).ToUniversalTime();

        logger.LogInformation("Iniciando cálculo de métricas de riesgo desde {From} hasta {To}", fromDate, toDate);

        try
        {
            var query = context.AuditLogs
                .AsNoTracking()
                .Where(a => a.EvaluatedAt >= fromDate && a.EvaluatedAt <= toDate);

            var totalEvaluations = await query.CountAsync(cancellationToken);

            if (totalEvaluations == 0)
            {
                logger.LogInformation("No se encontraron registros de auditoría en el rango especificado.");
                return Result.Success(new MetricsSummaryResult(
                    TotalEvaluations: 0,
                    AllowedCount: 0,
                    ChallengedCount: 0,
                    BlockedCount: 0,
                    AllowedPercent: 0.0,
                    ChallengedPercent: 0.0,
                    BlockedPercent: 0.0,
                    AverageRiskScore: 0.0,
                    UniqueUsers: 0,
                    RiskScoreSeries: Array.Empty<RiskScorePointResult>()
                ));
            }

            var allowedCount = await query.LongCountAsync(a => a.Verdict == Verdict.Allow, cancellationToken);
            var challengedCount = await query.LongCountAsync(a => a.Verdict == Verdict.Challenge, cancellationToken);
            var blockedCount = await query.LongCountAsync(a => a.Verdict == Verdict.Block, cancellationToken);

            var allowedPercent = Math.Round((double)allowedCount / totalEvaluations * 100, 1);
            var challengedPercent = Math.Round((double)challengedCount / totalEvaluations * 100, 1);
            var blockedPercent = Math.Round((double)blockedCount / totalEvaluations * 100, 1);

            var averageRiskScore = Math.Round((double)await query.AverageAsync(a => a.RiskScore, cancellationToken), 1);

            var uniqueUsers = await query
                .Where(a => a.UserId != null)
                .Select(a => a.UserId)
                .Distinct()
                .CountAsync(cancellationToken);

            // Proyección optimizada para la serie temporal (agrupamiento por hora)
            var rawLogs = await query
                .Select(a => new { a.EvaluatedAt, a.RiskScore })
                .ToListAsync(cancellationToken);

            var series = rawLogs
                .GroupBy(p => new DateTimeOffset(
                    p.EvaluatedAt.Year,
                    p.EvaluatedAt.Month,
                    p.EvaluatedAt.Day,
                    p.EvaluatedAt.Hour,
                    0, 0,
                    p.EvaluatedAt.Offset))
                .Select(g => new RiskScorePointResult(
                    Timestamp: g.Key,
                    AvgScore: Math.Round((double)g.Average(p => p.RiskScore), 1),
                    EvaluationCount: g.Count()))
                .OrderBy(p => p.Timestamp)
                .ToList();

            logger.LogInformation(
                "Cálculo de métricas completado. Total: {TotalEvaluations}, Allowed: {AllowedCount}, Challenged: {ChallengedCount}, Blocked: {BlockedCount}, AvgScore: {AverageRiskScore}",
                totalEvaluations, allowedCount, challengedCount, blockedCount, averageRiskScore);

            return Result.Success(new MetricsSummaryResult(
                TotalEvaluations: totalEvaluations,
                AllowedCount: allowedCount,
                ChallengedCount: challengedCount,
                BlockedCount: blockedCount,
                AllowedPercent: allowedPercent,
                ChallengedPercent: challengedPercent,
                BlockedPercent: blockedPercent,
                AverageRiskScore: averageRiskScore,
                UniqueUsers: uniqueUsers,
                RiskScoreSeries: series
            ));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ocurrió un error inesperado al calcular las métricas de riesgo.");
            return Result.Failure<MetricsSummaryResult>(
                new Error("UnhandledException", $"Error al calcular métricas: {ex.Message}"));
        }
    }
}
