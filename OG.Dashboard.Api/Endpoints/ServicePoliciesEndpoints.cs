using Domain.ValueObjects;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using OG.Dashboard.Api.Contracts.Common;
using OG.Dashboard.Api.Contracts.Policies;

namespace OG.Dashboard.Api.Endpoints;

/// <summary>
/// Asociación de políticas a servicios protegidos (HU-023 T-048) bajo
/// <c>/api/v1/services/{serviceId}/policies</c>, sobre la entidad de unión
/// <c>ServicePolicy</c>. La combinación (servicio, política) es única — lo garantiza
/// el índice único de la BD y se traduce a 409 CONFLICT.
/// <para>
/// El motor lee service_policies en cada evaluación (<c>IServicePolicyProvider</c>,
/// sin caché): asociar o desasociar surte efecto en la siguiente petición al Gateway.
/// </para>
/// </summary>
public static class ServicePoliciesEndpoints
{
    public static IEndpointRouteBuilder MapServicePoliciesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/services/{serviceId:guid}/policies")
            .WithTags("ServicePolicies")
            .WithOpenApi();

        group.MapGet("/", GetAll)
            .RequireAuthorization("ReadAccess")
            .WithName("GetServicePolicies")
            .WithSummary("Listar las políticas asociadas a un servicio protegido");

        group.MapPost("/", Associate)
            .RequireAuthorization("AdminOnly")
            .WithName("AssociatePolicyToService")
            .WithSummary("Asociar una política a un servicio protegido")
            .Produces<ServicePolicyDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapDelete("/{policyId:guid}", Disassociate)
            .RequireAuthorization("AdminOnly")
            .WithName("DisassociatePolicyFromService")
            .WithSummary("Desasociar una política de un servicio protegido")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private static async Task<IResult> GetAll(Guid serviceId, OmakaseDbContext db, CancellationToken ct)
    {
        var sid = ProtectedServiceId.From(serviceId);

        if (!await db.ProtectedServices.AnyAsync(s => s.Id == sid, ct))
            return ServiceNotFound(serviceId);

        var associations = await db.ServicePolicies.AsNoTracking()
            .Where(sp => sp.ServiceId == sid)
            .Include(sp => sp.AccessPolicy)
            .ToListAsync(ct);

        return Results.Ok(associations.Select(ToDto).ToList());
    }

    private static async Task<IResult> Associate(
        Guid serviceId,
        AssociatePolicyRequest request,
        OmakaseDbContext db,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(nameof(ServicePoliciesEndpoints));
        var sid = ProtectedServiceId.From(serviceId);
        var pid = AccessPolicyId.From(request.PolicyId);

        if (!await db.ProtectedServices.AnyAsync(s => s.Id == sid, ct))
            return ServiceNotFound(serviceId);

        var policy = await db.AccessPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid, ct);
        if (policy is null)
            return Results.NotFound(new ErrorResponse(
                "NOT_FOUND", $"Política '{request.PolicyId}' no encontrada.", TraceId()));

        if (await db.ServicePolicies.AnyAsync(sp => sp.ServiceId == sid && sp.PolicyId == pid, ct))
            return Conflict(serviceId, request.PolicyId);

        var association = new Domain.Entities.ServicePolicy
        {
            Id        = ServicePolicyId.New(),
            ServiceId = sid,
            PolicyId  = pid,
            IsEnabled = request.IsEnabled,
        };

        db.ServicePolicies.Add(association);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Carrera entre el pre-chequeo y el insert: el índice único
            // (service_id, policy_id) es la garantía real de unicidad (T-048). Esperado, no es error.
            return Conflict(serviceId, request.PolicyId);
        }
        catch (DbUpdateException ex)
        {
            // Cualquier otro fallo de persistencia NO es la carrera de unicidad: se registra
            // con contexto en vez de enmascararlo como un 409 mentiroso.
            logger.LogError(ex,
                "Error de persistencia al asociar la politica {PolicyId} al servicio {ServiceId} en service_policies.",
                request.PolicyId, serviceId);
            return InternalError("No se pudo asociar la política al servicio.");
        }

        logger.LogInformation(
            "Política {PolicyId} asociada al servicio {ServiceId} (isEnabled={IsEnabled}).",
            request.PolicyId, serviceId, request.IsEnabled);

        association.AccessPolicy = policy;
        return Results.Created(
            $"/api/v1/services/{serviceId}/policies/{request.PolicyId}", ToDto(association));
    }

    private static async Task<IResult> Disassociate(
        Guid serviceId, Guid policyId, OmakaseDbContext db, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(nameof(ServicePoliciesEndpoints));
        var sid = ProtectedServiceId.From(serviceId);
        var pid = AccessPolicyId.From(policyId);

        var association = await db.ServicePolicies
            .FirstOrDefaultAsync(sp => sp.ServiceId == sid && sp.PolicyId == pid, ct);

        if (association is null)
            return Results.NotFound(new ErrorResponse(
                "NOT_FOUND",
                $"No existe asociación entre el servicio '{serviceId}' y la política '{policyId}'.",
                TraceId()));

        db.ServicePolicies.Remove(association);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex,
                "Error de persistencia al desasociar la politica {PolicyId} del servicio {ServiceId}.",
                policyId, serviceId);
            return InternalError("No se pudo desasociar la política del servicio.");
        }

        logger.LogInformation(
            "Política {PolicyId} desasociada del servicio {ServiceId}.", policyId, serviceId);

        return Results.NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>True si el fallo es la violación del índice único (SQLSTATE 23505 de Postgres).</summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };

    private static ServicePolicyDto ToDto(Domain.Entities.ServicePolicy sp) => new(
        Id: sp.Id.Value,
        PolicyId: sp.PolicyId.Value,
        PolicyName: sp.AccessPolicy?.Name ?? string.Empty,
        PolicyType: sp.AccessPolicy?.Type.ToString() ?? string.Empty,
        Weight: sp.AccessPolicy?.Weight ?? 0m,
        IsEnabled: sp.IsEnabled,
        PolicyIsActive: sp.AccessPolicy?.IsActive ?? false);

    private static string TraceId() =>
        System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A";

    private static IResult ServiceNotFound(Guid serviceId) =>
        Results.NotFound(new ErrorResponse(
            "NOT_FOUND", $"Servicio protegido '{serviceId}' no encontrado.", TraceId()));

    private static IResult Conflict(Guid serviceId, Guid policyId) =>
        Results.Conflict(new ErrorResponse(
            "CONFLICT",
            $"La política '{policyId}' ya está asociada al servicio '{serviceId}'.",
            TraceId()));

    private static IResult InternalError(string message) =>
        Results.Json(new ErrorResponse("INTERNAL_ERROR", message, TraceId()),
            statusCode: StatusCodes.Status500InternalServerError);
}
