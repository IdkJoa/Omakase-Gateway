using OG.Dashboard.Api.Contracts.Metrics;
using OG.Dashboard.Features.Metrics;

namespace OG.Dashboard.Api.Endpoints;

/// <summary>
/// Endpoints de métricas y KPIs del Dashboard principal (HU-021 / T-062).
/// GET /api/v1/metrics/summary — Tarjetas de KPI y serie temporal del Risk Score calculadas sobre audit_logs.
/// </summary>
public static class MetricsEndpoints
{
    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/metrics")
            .WithTags("Metrics")
            .WithOpenApi();

        group.MapGet("/summary", GetSummary)
            .RequireAuthorization("ReadAccess")
            .WithName("GetMetricsSummary")
            .WithSummary("Obtener resumen de KPIs del Dashboard")
            .WithDescription(
                "Devuelve los KPIs principales calculados sobre audit_logs: totales por veredicto, porcentajes, " +
                "Risk Score promedio y la serie temporal para el gráfico de tendencia.");

        return app;
    }

    private static async Task<IResult> GetSummary(
        GetMetricsSummaryHandler handler,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var result = await handler.GetMetricsSummaryAsync(from, to, ct);

        if (result.IsFailure)
        {
            return Results.Problem(
                detail: result.Error.Description,
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var m = result.Value;
        var dto = new MetricsSummaryDto(
            TotalEvaluations: m.TotalEvaluations,
            AllowedCount: m.AllowedCount,
            ChallengedCount: m.ChallengedCount,
            BlockedCount: m.BlockedCount,
            AllowedPercent: m.AllowedPercent,
            ChallengedPercent: m.ChallengedPercent,
            BlockedPercent: m.BlockedPercent,
            AverageRiskScore: m.AverageRiskScore,
            UniqueUsers: m.UniqueUsers,
            RiskScoreSeries: m.RiskScoreSeries.Select(p => new RiskScorePointDto(
                Timestamp: p.Timestamp,
                AvgScore: p.AvgScore,
                EvaluationCount: p.EvaluationCount
            )).ToList()
        );

        return Results.Ok(dto);
    }
}
