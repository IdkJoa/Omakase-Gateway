using System.Text.Json;
using Application.Common.RiskEngine.Rules.Validation;
using Application.Common.Security;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using OG.Dashboard.Api.Contracts.Common;
using OG.Dashboard.Api.Contracts.Policies;

namespace OG.Dashboard.Api.Endpoints;

/// <summary>
/// CRUD real de políticas de acceso (HU-023 T-047) bajo <c>/api/v1/policies</c>,
/// sobre la entidad <see cref="AccessPolicy"/>. Sustituye el mock Contract-First (T-088)
/// conservando sus contratos (<see cref="PolicyDto"/>, <see cref="PagedResponse{T}"/>,
/// <see cref="ErrorResponse"/>).
/// <para>
/// Decisiones (SRS §9): la config JSONB se valida por tipo con
/// <see cref="IPolicyConfigValidator"/> (lo que el motor no sabe evaluar no se persiste);
/// <c>created_by</c> se resuelve del token Keycloak vía <see cref="ICurrentUserService"/>;
/// el DELETE es soft-delete (<c>is_active=false</c>, T-047); el motor lee las políticas
/// de la BD en cada evaluación (sin caché), por lo que todo cambio surte efecto en la
/// siguiente petición sin invalidación.
/// </para>
/// <para>
/// Autorización: mutaciones = policy <c>AdminOnly</c>; lecturas = <c>ReadAccess</c>
/// (ADMIN o VIEWER). HU-028 (T-058) re-implementará ambas policies contra
/// <c>user_roles</c> sin tocar estos endpoints.
/// </para>
/// </summary>
public static class PoliciesEndpoints
{
    public static IEndpointRouteBuilder MapPoliciesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/policies")
            .WithTags("Policies")
            .WithOpenApi();

        group.MapGet("/", GetAll)
            .RequireAuthorization("ReadAccess")
            .WithName("GetPolicies")
            .WithSummary("Listar políticas de acceso")
            .WithDescription("Devuelve la lista paginada de políticas de acceso. Soporta filtro por tipo e isActive.");

        group.MapGet("/{id:guid}", GetById)
            .RequireAuthorization("ReadAccess")
            .WithName("GetPolicyById")
            .WithSummary("Obtener una política de acceso por ID");

        group.MapPost("/", Create)
            .RequireAuthorization("AdminOnly")
            .WithName("CreatePolicy")
            .WithSummary("Crear una nueva política de acceso")
            .Produces<PolicyDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapPut("/{id:guid}", Update)
            .RequireAuthorization("AdminOnly")
            .WithName("UpdatePolicy")
            .WithSummary("Actualizar una política de acceso existente")
            .Produces<PolicyDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        group.MapDelete("/{id:guid}", Delete)
            .RequireAuthorization("AdminOnly")
            .WithName("DeletePolicy")
            .WithSummary("Eliminar (soft-delete) una política de acceso")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private static async Task<IResult> GetAll(
        OmakaseDbContext db,
        CancellationToken ct,
        int page = 1,
        int pageSize = 25,
        string? type = null,
        bool? isActive = null)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 100) pageSize = 25;

        var query = db.AccessPolicies.AsNoTracking().Include(p => p.CreatedBy).AsQueryable();

        if (!string.IsNullOrWhiteSpace(type))
        {
            if (!TryParseType(type, out var parsedType))
                return ValidationError($"Tipo '{type}' no válido. Valores: {ValidTypes()}.");
            query = query.Where(p => p.Type == parsedType);
        }

        if (isActive.HasValue)
            query = query.Where(p => p.IsActive == isActive.Value);

        var total = await query.CountAsync(ct);
        var pageEntities = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var data = pageEntities.Select(ToDto).ToList();

        return Results.Ok(new PagedResponse<PolicyDto>(page, pageSize, total, data));
    }

    private static async Task<IResult> GetById(Guid id, OmakaseDbContext db, CancellationToken ct)
    {
        var policyId = AccessPolicyId.From(id);
        var policy = await db.AccessPolicies.AsNoTracking()
            .Include(p => p.CreatedBy)
            .FirstOrDefaultAsync(p => p.Id == policyId, ct);

        return policy is null ? NotFound(id) : Results.Ok(ToDto(policy));
    }

    private static async Task<IResult> Create(
        UpsertPolicyRequest request,
        OmakaseDbContext db,
        ICurrentUserService currentUser,
        IEnumerable<IPolicyConfigValidator> validators,
        HttpContext http,
        CancellationToken ct)
    {
        var validation = ValidateRequest(request, validators, out var type, out var config);
        if (validation is not null)
            return validation;

        // T-047: created_by sale del token del administrador autenticado (JIT provisioning).
        var admin = await currentUser.GetOrProvisionAsync(http.User, ct);
        if (admin is null)
            return Results.Json(
                new ErrorResponse("UNAUTHORIZED",
                    "No se pudo resolver la identidad del administrador desde el token.", TraceId()),
                statusCode: StatusCodes.Status401Unauthorized);

        var policy = new AccessPolicy
        {
            Id          = AccessPolicyId.New(),
            Name        = request.Name.Trim(),
            Type        = type,
            Config      = config!,
            Weight      = request.Weight,
            IsActive    = request.IsActive,
            CreatedById = admin.Id,
        };

        db.AccessPolicies.Add(policy);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/policies/{policy.Id.Value}", ToDto(policy, admin.Username));
    }

    private static async Task<IResult> Update(
        Guid id,
        UpsertPolicyRequest request,
        OmakaseDbContext db,
        IEnumerable<IPolicyConfigValidator> validators,
        CancellationToken ct)
    {
        var validation = ValidateRequest(request, validators, out var type, out var config);
        if (validation is not null)
            return validation;

        var policyId = AccessPolicyId.From(id);
        var policy = await db.AccessPolicies
            .Include(p => p.CreatedBy)
            .FirstOrDefaultAsync(p => p.Id == policyId, ct);

        if (policy is null)
            return NotFound(id);

        policy.Name     = request.Name.Trim();
        policy.Type     = type;
        policy.Config   = config!;
        policy.Weight   = request.Weight;
        policy.IsActive = request.IsActive;

        await db.SaveChangesAsync(ct);

        return Results.Ok(ToDto(policy));
    }

    private static async Task<IResult> Delete(Guid id, OmakaseDbContext db, CancellationToken ct)
    {
        var policyId = AccessPolicyId.From(id);
        var policy = await db.AccessPolicies.FirstOrDefaultAsync(p => p.Id == policyId, ct);

        if (policy is null)
            return NotFound(id);

        // Soft-delete (T-047): el motor deja de evaluarla en la siguiente petición;
        // el histórico y las asociaciones service_policies se conservan.
        policy.IsActive = false;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    // ── Validación ────────────────────────────────────────────────────────────

    /// <summary>
    /// Validación común de POST/PUT: nombre, tipo, weight en [0,1] (T-047) y config
    /// JSONB coherente con lo que parsea el evaluador del tipo. Devuelve el 400 listo
    /// o null si el request es válido.
    /// </summary>
    private static IResult? ValidateRequest(
        UpsertPolicyRequest request,
        IEnumerable<IPolicyConfigValidator> validators,
        out PolicyType type,
        out JsonDocument? config)
    {
        type = default;
        config = null;

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length is < 3 or > 100)
            return ValidationError("'name' es requerido (3–100 caracteres).");

        if (!TryParseType(request.Type, out type))
            return ValidationError($"Tipo '{request.Type}' no válido. Valores: {ValidTypes()}.");

        if (request.Weight is < 0m or > 1m)
            return ValidationError("'weight' debe estar en el rango [0,1] (T-047).");

        try
        {
            config = JsonSerializer.SerializeToDocument(request.Config);
        }
        catch (Exception)
        {
            return ValidationError("'config' no es JSON válido.");
        }

        if (config.RootElement.ValueKind != JsonValueKind.Object)
            return ValidationError("'config' debe ser un objeto JSON (ej. { \"allowed_countries\": [\"DO\"] }).");

        var typeCopy = type;
        var validator = validators.FirstOrDefault(v => v.Type == typeCopy);
        if (validator is not null)
        {
            var errors = validator.Validate(config);
            if (errors.Count > 0)
                return ValidationError(string.Join(" ", errors));
        }

        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PolicyDto ToDto(AccessPolicy p) => ToDto(p, p.CreatedBy?.Username ?? string.Empty);

    private static PolicyDto ToDto(AccessPolicy p, string createdByUsername) => new(
        Id: p.Id.Value,
        Name: p.Name,
        Type: p.Type.ToString(),
        Config: p.Config.RootElement.Clone(),
        Weight: p.Weight,
        IsActive: p.IsActive,
        CreatedById: p.CreatedById.Value,
        CreatedByUsername: createdByUsername,
        CreatedAt: p.CreatedAt);

    /// <summary>
    /// Acepta el nombre del enum (<c>Geofence</c>, contrato del mock) y la grafía
    /// del SRS/BD (<c>GEOFENCE</c>, <c>TIME_WINDOW</c>), sin distinción de mayúsculas.
    /// </summary>
    private static bool TryParseType(string raw, out PolicyType type) =>
        Enum.TryParse(raw.Replace("_", string.Empty), ignoreCase: true, out type);

    private static string ValidTypes() => string.Join(" | ", Enum.GetNames<PolicyType>());

    private static string TraceId() =>
        System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A";

    private static IResult ValidationError(string message) =>
        Results.BadRequest(new ErrorResponse("VALIDATION_ERROR", message, TraceId()));

    private static IResult NotFound(Guid id) =>
        Results.NotFound(new ErrorResponse("NOT_FOUND", $"Política '{id}' no encontrada.", TraceId()));
}
