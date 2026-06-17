using OG.Dashboard.Api.Contracts.Metrics;

namespace OG.Dashboard.Api.Endpoints;

/// <summary>
/// Endpoints mock de métricas y KPIs del Dashboard principal.
/// GET /api/v1/metrics/summary — Tarjetas de KPI y serie temporal del Risk Score.
/// </summary>
public static class MetricsEndpoints
{
    // ── Datos mock ────────────────────────────────────────────────────────────

    private static readonly MetricsSummaryDto MockSummary = new(
        TotalEvaluations: 14_320,
        AllowedCount: 11_200,
        ChallengedCount: 2_500,
        BlockedCount: 620,
        AllowedPercent: 78.2,
        ChallengedPercent: 17.5,
        BlockedPercent: 4.3,
        AverageRiskScore: 34.7,
        UniqueUsers: 312,
        RiskScoreSeries:
        [
            new(new DateTimeOffset(2026, 5, 29,  0, 0, 0, TimeSpan.Zero), 28.4, 580),
            new(new DateTimeOffset(2026, 5, 29,  1, 0, 0, TimeSpan.Zero), 26.1, 210),
            new(new DateTimeOffset(2026, 5, 29,  2, 0, 0, TimeSpan.Zero), 24.8, 130),
            new(new DateTimeOffset(2026, 5, 29,  6, 0, 0, TimeSpan.Zero), 31.2, 620),
            new(new DateTimeOffset(2026, 5, 29,  8, 0, 0, TimeSpan.Zero), 38.9, 1200),
            new(new DateTimeOffset(2026, 5, 29, 10, 0, 0, TimeSpan.Zero), 41.3, 1540),
            new(new DateTimeOffset(2026, 5, 29, 12, 0, 0, TimeSpan.Zero), 36.7, 1820),
            new(new DateTimeOffset(2026, 5, 29, 14, 0, 0, TimeSpan.Zero), 44.1, 1630),
            new(new DateTimeOffset(2026, 5, 29, 16, 0, 0, TimeSpan.Zero), 39.5, 1480),
            new(new DateTimeOffset(2026, 5, 29, 18, 0, 0, TimeSpan.Zero), 33.2, 1100),
            new(new DateTimeOffset(2026, 5, 29, 20, 0, 0, TimeSpan.Zero), 29.8,  820),
            new(new DateTimeOffset(2026, 5, 29, 22, 0, 0, TimeSpan.Zero), 27.1,  510),
        ]
    );

    // ── Registro de endpoints ─────────────────────────────────────────────────

    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/metrics")
            .WithTags("Metrics")
            .WithOpenApi();

        // GET /api/v1/metrics/summary
        group.MapGet("/summary", GetSummary)
            .WithName("GetMetricsSummary")
            .WithSummary("Obtener resumen de KPIs del Dashboard")
            .WithDescription(
                "Devuelve los KPIs principales: totales por veredicto, porcentajes, " +
                "Risk Score promedio y la serie temporal para el gráfico de tendencia.");

        return app;
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private static IResult GetSummary() => Results.Ok(MockSummary);
}
