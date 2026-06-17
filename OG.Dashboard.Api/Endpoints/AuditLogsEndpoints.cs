using OG.Dashboard.Api.Contracts.AuditLogs;
using OG.Dashboard.Api.Contracts.Common;

namespace OG.Dashboard.Api.Endpoints;

/// <summary>
/// Endpoints mock de exploración de logs de auditoría.
/// GET /api/v1/logs — Contrato definido en SRS §4.1.
/// Devuelve datos estáticos representativos para que el frontend Angular avance en paralelo.
/// </summary>
public static class AuditLogsEndpoints
{
    // ── Datos mock ────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<AuditLogDto> MockLogs =
    [
        new(
            EvaluationId: Guid.Parse("a3f1c2e8-7b94-4d2a-9c1f-0e5b6d8a1234"),
            Timestamp: new DateTimeOffset(2026, 5, 29, 14, 32, 7, TimeSpan.Zero),
            UserId: "user-2048",
            Username: "jperez",
            ServiceName: "orders-service",
            SourceIp: "190.166.12.45",
            Geo: new GeoDto("DO", "Santo Domingo", 18.4861, -69.9312),
            UserAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64)",
            PolicyScore: 35,
            AnomalyScore: 82,
            RiskScore: 71,
            Verdict: "CHALLENGE",
            TriggeredRules: ["IMPOSSIBLE_TRAVEL", "ANOMALY_VOLUME"]
        ),
        new(
            EvaluationId: Guid.Parse("b1c2d3e4-f5a6-7b8c-9d0e-1f2a3b4c5d6e"),
            Timestamp: new DateTimeOffset(2026, 5, 29, 14, 28, 0, TimeSpan.Zero),
            UserId: "user-1001",
            Username: "mgarcia",
            ServiceName: "payments-service",
            SourceIp: "8.8.8.8",
            Geo: new GeoDto("US", "Mountain View", 37.3861, -122.0839),
            UserAgent: "PostmanRuntime/7.32.0",
            PolicyScore: 90,
            AnomalyScore: 88,
            RiskScore: 89,
            Verdict: "BLOCK",
            TriggeredRules: ["IP_BLACKLIST", "GEOFENCE", "ANOMALY_VOLUME"]
        ),
        new(
            EvaluationId: Guid.Parse("c2d3e4f5-a6b7-8c9d-0e1f-2a3b4c5d6e7f"),
            Timestamp: new DateTimeOffset(2026, 5, 29, 14, 15, 33, TimeSpan.Zero),
            UserId: "user-3312",
            Username: "lrodriguez",
            ServiceName: "inventory-service",
            SourceIp: "192.168.1.100",
            Geo: new GeoDto("DO", "Santiago", 19.4517, -70.6970),
            UserAgent: "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0)",
            PolicyScore: 10,
            AnomalyScore: 15,
            RiskScore: 12,
            Verdict: "ALLOW",
            TriggeredRules: []
        ),
        new(
            EvaluationId: Guid.Parse("d3e4f5a6-b7c8-9d0e-1f2a-3b4c5d6e7f8a"),
            Timestamp: new DateTimeOffset(2026, 5, 29, 13, 55, 0, TimeSpan.Zero),
            UserId: null,
            Username: null,
            ServiceName: null,
            SourceIp: "45.33.32.156",
            Geo: new GeoDto("US", "Fremont", 37.5485, -121.9886),
            UserAgent: "python-requests/2.31.0",
            PolicyScore: 100,
            AnomalyScore: 95,
            RiskScore: 98,
            Verdict: "BLOCK",
            TriggeredRules: ["IP_BLACKLIST", "GEOFENCE", "RATE_LIMIT"]
        ),
        new(
            EvaluationId: Guid.Parse("e4f5a6b7-c8d9-0e1f-2a3b-4c5d6e7f8a9b"),
            Timestamp: new DateTimeOffset(2026, 5, 29, 13, 30, 47, TimeSpan.Zero),
            UserId: "user-0099",
            Username: "alopez",
            ServiceName: "orders-service",
            SourceIp: "10.0.0.55",
            Geo: new GeoDto("DO", "Santo Domingo", 18.4861, -69.9312),
            UserAgent: "Angular/17.0 (internal)",
            PolicyScore: 8,
            AnomalyScore: 12,
            RiskScore: 10,
            Verdict: "ALLOW",
            TriggeredRules: []
        ),
    ];

    // ── Registro de endpoints ─────────────────────────────────────────────────

    public static IEndpointRouteBuilder MapAuditLogsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/logs")
            .WithTags("Audit Logs")
            .WithOpenApi();

        // GET /api/v1/logs — Lista paginada de logs con filtros opcionales.
        group.MapGet("/", GetLogs)
            .WithName("GetAuditLogs")
            .WithSummary("Listar logs de auditoría paginados")
            .WithDescription(
                "Devuelve una lista paginada de logs de evaluación de riesgo. " +
                "Soporta filtros por veredicto, usuario, rango de fechas, IP y servicio. " +
                "Contrato definido en el SRS §4.1.");

        // GET /api/v1/logs/{evaluationId} — Detalle de un log individual.
        group.MapGet("/{evaluationId:guid}", GetLogById)
            .WithName("GetAuditLogById")
            .WithSummary("Obtener detalle de un log de auditoría por EvaluationId");

        return app;
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private static IResult GetLogs(
        int page = 1,
        int pageSize = 25,
        string? verdict = null,
        string? userId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? sourceIp = null,
        string? serviceName = null)
    {
        // Validación básica de paginación
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 100) pageSize = 25;

        // Aplicar filtros sobre la colección mock
        var filtered = MockLogs.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(verdict))
            filtered = filtered.Where(l => l.Verdict.Equals(verdict, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(userId))
            filtered = filtered.Where(l => l.UserId == userId);

        if (from.HasValue)
            filtered = filtered.Where(l => l.Timestamp >= from.Value);

        if (to.HasValue)
            filtered = filtered.Where(l => l.Timestamp <= to.Value);

        if (!string.IsNullOrWhiteSpace(sourceIp))
            filtered = filtered.Where(l => l.SourceIp == sourceIp);

        if (!string.IsNullOrWhiteSpace(serviceName))
            filtered = filtered.Where(l => l.ServiceName?.Equals(serviceName, StringComparison.OrdinalIgnoreCase) == true);

        var list = filtered.ToList();
        var totalRecords = list.Count;
        var data = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Results.Ok(new PagedResponse<AuditLogDto>(page, pageSize, totalRecords, data));
    }

    private static IResult GetLogById(Guid evaluationId)
    {
        var log = MockLogs.FirstOrDefault(l => l.EvaluationId == evaluationId);

        if (log is null)
            return Results.NotFound(new Contracts.Common.ErrorResponse(
                ErrorCode: "NOT_FOUND",
                Message: $"No se encontró un log con EvaluationId '{evaluationId}'.",
                TraceId: System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        return Results.Ok(log);
    }
}
