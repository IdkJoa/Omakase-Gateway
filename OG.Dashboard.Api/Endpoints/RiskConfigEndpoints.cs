using OG.Dashboard.Api.Contracts.Common;
using OG.Dashboard.Api.Contracts.RiskConfig;

namespace OG.Dashboard.Api.Endpoints;

/// <summary>
/// Endpoints mock de configuración del motor de evaluación de riesgo.
/// GET /api/v1/risk-config  — Leer la configuración vigente.
/// PUT /api/v1/risk-config  — Actualizar pesos y umbrales.
///
/// La entidad RiskScoreConfig es una fila única en BD; el sistema expone
/// un único resource (sin colección) para representar esta singularidad.
/// Fórmula: RiskScore = Wp*PolicyScore + Wa*AnomalyScore + ColdStartPenalty(n)
/// </summary>
public static class RiskConfigEndpoints
{
    // ── Datos mock ────────────────────────────────────────────────────────────

    // Estado mutable en memoria para el mock — simula lectura/escritura en BD.
    private static RiskScoreConfigDto _current = new(
        Id: Guid.Parse("c0de0001-0000-0000-0000-000000000000"),
        PolicyWeight: 0.6m,
        AnomalyWeight: 0.4m,
        ColdStartPenalty: 30.0m,
        ColdStartN: 10,
        BlockThreshold: 75.0m,
        ChallengeThreshold: 50.0m,
        UpdatedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)
    );

    // ── Registro de endpoints ─────────────────────────────────────────────────

    public static IEndpointRouteBuilder MapRiskConfigEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/risk-config")
            .WithTags("Risk Score Configuration")
            .WithOpenApi();

        // GET /api/v1/risk-config
        group.MapGet("/", Get)
            .WithName("GetRiskConfig")
            .WithSummary("Obtener la configuración vigente del motor de riesgo")
            .WithDescription(
                "Devuelve los pesos (PolicyWeight, AnomalyWeight), penalización cold-start " +
                "y umbrales de veredicto (BlockThreshold, ChallengeThreshold). " +
                "Corresponde a la fila única de risk_score_config en PostgreSQL.");

        // PUT /api/v1/risk-config
        group.MapPut("/", Update)
            .WithName("UpdateRiskConfig")
            .WithSummary("Actualizar la configuración del motor de riesgo")
            .WithDescription(
                "Persiste nuevos pesos y umbrales. " +
                "Validación: PolicyWeight + AnomalyWeight debe ser igual a 1. " +
                "ChallengeThreshold debe ser menor que BlockThreshold.")
            .Produces<RiskScoreConfigDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        return app;
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private static IResult Get() => Results.Ok(_current);

    private static IResult Update(UpdateRiskConfigRequest request)
    {
        // Validación de negocio: pesos deben sumar 1
        var weightSum = request.PolicyWeight + request.AnomalyWeight;
        if (Math.Abs((double)weightSum - 1.0) > 0.001)
            return Results.BadRequest(new ErrorResponse(
                ErrorCode: "VALIDATION_ERROR",
                Message: $"PolicyWeight ({request.PolicyWeight}) + AnomalyWeight ({request.AnomalyWeight}) debe ser igual a 1. Suma actual: {weightSum}.",
                TraceId: System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        // Validación de negocio: umbrales coherentes
        if (request.ChallengeThreshold >= request.BlockThreshold)
            return Results.BadRequest(new ErrorResponse(
                ErrorCode: "VALIDATION_ERROR",
                Message: $"ChallengeThreshold ({request.ChallengeThreshold}) debe ser menor que BlockThreshold ({request.BlockThreshold}).",
                TraceId: System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        // Mock: actualizar el estado en memoria.
        _current = _current with
        {
            PolicyWeight = request.PolicyWeight,
            AnomalyWeight = request.AnomalyWeight,
            ColdStartPenalty = request.ColdStartPenalty,
            ColdStartN = request.ColdStartN,
            BlockThreshold = request.BlockThreshold,
            ChallengeThreshold = request.ChallengeThreshold,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        return Results.Ok(_current);
    }
}
