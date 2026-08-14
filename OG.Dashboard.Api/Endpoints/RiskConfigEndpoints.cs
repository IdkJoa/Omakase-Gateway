using Domain.Entities;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using OG.Dashboard.Api.Contracts.Common;
using OG.Dashboard.Api.Contracts.RiskConfig;

namespace OG.Dashboard.Api.Endpoints;

/// <summary>
/// Configuración del motor de evaluación de riesgo (HU-025 / T-052), sobre la fila única
/// <c>risk_score_config</c>. Sustituye el mock Contract-First (T-088) conservando su contrato
/// (nombres de campo y ruta <c>/api/v1/risk-config</c>) para no romper el front ya construido
/// (editor de pesos de HU-045).
/// <para>
/// La entidad es una fila única (singleton global). El motor lee la config en cada evaluación
/// (<c>RiskConfigProvider</c>, sin caché), por lo que un PUT surte efecto en la siguiente
/// petición sin invalidación (criterio de aceptación de HU-025).
/// </para>
/// <para>Autorización: GET = <c>ReadAccess</c> (ADMIN|VIEWER); PUT = <c>AdminOnly</c>.</para>
/// </summary>
public static class RiskConfigEndpoints
{
    private const decimal WeightSumTolerance = 0.001m;

    public static IEndpointRouteBuilder MapRiskConfigEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/risk-config")
            .WithTags("Risk Score Configuration")
            .WithOpenApi();

        group.MapGet("/", Get)
            .RequireAuthorization("ReadAccess")
            .WithName("GetRiskConfig")
            .WithSummary("Obtener la configuración vigente del motor de riesgo");

        group.MapPut("/", Update)
            .RequireAuthorization("AdminOnly")
            .WithName("UpdateRiskConfig")
            .WithSummary("Actualizar pesos y umbrales del Risk Score")
            .Produces<RiskScoreConfigDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<IResult> Get(
        OmakaseDbContext db, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        // Fila única (singleton): OrderBy explícito para una lectura determinista y silenciar
        // la advertencia de EF sobre FirstOrDefault sin orden.
        var config = await db.RiskScoreConfigs.AsNoTracking()
            .OrderBy(r => r.UpdatedAt).FirstOrDefaultAsync(ct);
        if (config is null)
        {
            loggerFactory.CreateLogger(nameof(RiskConfigEndpoints))
                .LogWarning("risk_score_config no tiene filas al leer la configuración; falta el seed (HU-006).");
            return ConfigMissing();
        }

        return Results.Ok(ToDto(config));
    }

    private static async Task<IResult> Update(
        UpdateRiskConfigRequest request, OmakaseDbContext db, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var validation = Validate(request);
        if (validation is not null)
            return validation;

        var logger = loggerFactory.CreateLogger(nameof(RiskConfigEndpoints));

        // Fila única: se lee rastreada para actualizarla en sitio (no AsNoTracking).
        // OrderBy explícito → lectura determinista y sin la advertencia de EF.
        var config = await db.RiskScoreConfigs.OrderBy(r => r.UpdatedAt).FirstOrDefaultAsync(ct);
        if (config is null)
        {
            logger.LogWarning("risk_score_config no tiene filas al actualizar; falta el seed (HU-006).");
            return ConfigMissing();
        }

        config.PolicyWeight       = request.PolicyWeight;
        config.AnomalyWeight      = request.AnomalyWeight;
        config.ColdStartPenalty   = request.ColdStartPenalty;
        config.ColdStartN         = request.ColdStartN;
        config.BlockThreshold     = request.BlockThreshold;
        config.ChallengeThreshold = request.ChallengeThreshold;
        config.UpdatedAt          = DateTimeOffset.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Error de persistencia al actualizar risk_score_config.");
            return InternalError("No se pudo guardar la configuración del Risk Score.");
        }

        logger.LogInformation(
            "risk_score_config actualizada: Wp={PolicyWeight} Wa={AnomalyWeight} allow(challengeThreshold)={AllowCeiling} challenge(blockThreshold)={ChallengeCeiling}.",
            request.PolicyWeight, request.AnomalyWeight, request.ChallengeThreshold, request.BlockThreshold);

        return Results.Ok(ToDto(config));
    }

    /// <summary><c>ChallengeThreshold</c> es el techo de ALLOW y <c>BlockThreshold</c> el de CHALLENGE (T-052); por eso se exige ChallengeThreshold &lt; BlockThreshold.</summary>
    private static IResult? Validate(UpdateRiskConfigRequest r)
    {
        if (r.PolicyWeight is < 0m or > 1m || r.AnomalyWeight is < 0m or > 1m)
            return Error("Los pesos (PolicyWeight, AnomalyWeight) deben estar en el rango [0,1].");

        if (Math.Abs((r.PolicyWeight + r.AnomalyWeight) - 1m) > WeightSumTolerance)
            return Error($"PolicyWeight ({r.PolicyWeight}) + AnomalyWeight ({r.AnomalyWeight}) " +
                         $"debe ser igual a 1.0 (tolerancia ±{WeightSumTolerance}).");

        if (!IsIntegerInRange(r.ChallengeThreshold, 0, 100))
            return Error("ChallengeThreshold debe ser un entero en el rango [0,100].");

        if (!IsIntegerInRange(r.BlockThreshold, 0, 100))
            return Error("BlockThreshold debe ser un entero en el rango [0,100].");

        if (r.ChallengeThreshold >= r.BlockThreshold)
            return Error($"ChallengeThreshold ({r.ChallengeThreshold}), el techo de ALLOW, debe ser menor " +
                         $"que BlockThreshold ({r.BlockThreshold}), el techo de CHALLENGE.");

        if (r.ColdStartPenalty is < 0m or > 100m)
            return Error("ColdStartPenalty debe estar en el rango [0,100].");

        if (r.ColdStartN < 1)
            return Error("ColdStartN debe ser un entero mayor o igual a 1.");

        return null;
    }

    private static bool IsIntegerInRange(decimal value, decimal min, decimal max) =>
        value == Math.Truncate(value) && value >= min && value <= max;

    private static RiskScoreConfigDto ToDto(RiskScoreConfig c) => new(
        Id: c.Id.Value,
        PolicyWeight: c.PolicyWeight,
        AnomalyWeight: c.AnomalyWeight,
        ColdStartPenalty: c.ColdStartPenalty,
        ColdStartN: c.ColdStartN,
        BlockThreshold: c.BlockThreshold,
        ChallengeThreshold: c.ChallengeThreshold,
        UpdatedAt: c.UpdatedAt);

    private static string TraceId() =>
        System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A";

    private static IResult Error(string message) =>
        Results.BadRequest(new ErrorResponse("VALIDATION_ERROR", message, TraceId()));

    private static IResult ConfigMissing() =>
        Results.NotFound(new ErrorResponse(
            "NOT_FOUND",
            "risk_score_config no tiene filas. Ejecuta el seed (HU-006) antes de operar.",
            TraceId()));

    private static IResult InternalError(string message) =>
        Results.Json(new ErrorResponse("INTERNAL_ERROR", message, TraceId()),
            statusCode: StatusCodes.Status500InternalServerError);
}
