using OG.Dashboard.Api.Contracts.Common;
using OG.Dashboard.Api.Contracts.Policies;

namespace OG.Dashboard.Api.Endpoints;

/// <summary>
/// Endpoints mock CRUD de políticas de acceso.
/// CRUD bajo /api/v1/policies — corresponde a la entidad AccessPolicy del dominio.
/// Los datos mock reflejan los tipos de política definidos en el enum PolicyType.
/// </summary>
public static class PoliciesEndpoints
{
    // ── Datos mock ────────────────────────────────────────────────────────────

    private static readonly List<PolicyDto> MockPolicies =
    [
        new(
            Id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name: "Geofence — Solo República Dominicana",
            Type: "Geofence",
            Config: new { allowedCountries = new[] { "DO" } },
            Weight: 2.5m,
            IsActive: true,
            CreatedById: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            CreatedByUsername: "admin.omakase",
            CreatedAt: new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero)
        ),
        new(
            Id: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Name: "Horario Laboral — 8am a 8pm L-V",
            Type: "TimeWindow",
            Config: new { startHour = 8, endHour = 20, daysOfWeek = new[] { 1, 2, 3, 4, 5 } },
            Weight: 1.5m,
            IsActive: true,
            CreatedById: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            CreatedByUsername: "admin.omakase",
            CreatedAt: new DateTimeOffset(2026, 5, 2, 10, 30, 0, TimeSpan.Zero)
        ),
        new(
            Id: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Name: "Viaje Imposible — Velocidad máx 900 km/h",
            Type: "ImpossibleTravel",
            Config: new { maxSpeedKmh = 900 },
            Weight: 3.0m,
            IsActive: true,
            CreatedById: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            CreatedByUsername: "admin.omakase",
            CreatedAt: new DateTimeOffset(2026, 5, 3, 11, 0, 0, TimeSpan.Zero)
        ),
        new(
            Id: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Name: "Fingerprint — Bloquear cambios de dispositivo",
            Type: "Fingerprint",
            Config: new { maxDevicesPerSession = 1 },
            Weight: 2.0m,
            IsActive: false,
            CreatedById: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            CreatedByUsername: "admin.omakase",
            CreatedAt: new DateTimeOffset(2026, 5, 10, 8, 0, 0, TimeSpan.Zero)
        ),
    ];

    // ── Registro de endpoints ─────────────────────────────────────────────────

    public static IEndpointRouteBuilder MapPoliciesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/policies")
            .WithTags("Policies")
            .WithOpenApi();

        // GET /api/v1/policies
        group.MapGet("/", GetAll)
            .WithName("GetPolicies")
            .WithSummary("Listar políticas de acceso")
            .WithDescription("Devuelve la lista paginada de políticas de acceso. Soporta filtro por tipo e isActive.");

        // GET /api/v1/policies/{id}
        group.MapGet("/{id:guid}", GetById)
            .WithName("GetPolicyById")
            .WithSummary("Obtener una política de acceso por ID");

        // POST /api/v1/policies
        group.MapPost("/", Create)
            .WithName("CreatePolicy")
            .WithSummary("Crear una nueva política de acceso")
            .Produces<PolicyDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        // PUT /api/v1/policies/{id}
        group.MapPut("/{id:guid}", Update)
            .WithName("UpdatePolicy")
            .WithSummary("Actualizar una política de acceso existente")
            .Produces<PolicyDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        // DELETE /api/v1/policies/{id}
        group.MapDelete("/{id:guid}", Delete)
            .WithName("DeletePolicy")
            .WithSummary("Eliminar (soft-delete) una política de acceso")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private static IResult GetAll(
        int page = 1,
        int pageSize = 25,
        string? type = null,
        bool? isActive = null)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 100) pageSize = 25;

        var filtered = MockPolicies.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(type))
            filtered = filtered.Where(p => p.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

        if (isActive.HasValue)
            filtered = filtered.Where(p => p.IsActive == isActive.Value);

        var list = filtered.ToList();
        var data = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Results.Ok(new PagedResponse<PolicyDto>(page, pageSize, list.Count, data));
    }

    private static IResult GetById(Guid id)
    {
        var policy = MockPolicies.FirstOrDefault(p => p.Id == id);
        if (policy is null)
            return Results.NotFound(new ErrorResponse("NOT_FOUND", $"Política '{id}' no encontrada.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        return Results.Ok(policy);
    }

    private static IResult Create(UpsertPolicyRequest request)
    {
        // Mock: devuelve un PolicyDto con ID generado, representando la entidad creada.
        var created = new PolicyDto(
            Id: Guid.NewGuid(),
            Name: request.Name,
            Type: request.Type,
            Config: request.Config,
            Weight: request.Weight,
            IsActive: request.IsActive,
            CreatedById: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            CreatedByUsername: "admin.omakase",
            CreatedAt: DateTimeOffset.UtcNow
        );

        return Results.Created($"/api/v1/policies/{created.Id}", created);
    }

    private static IResult Update(Guid id, UpsertPolicyRequest request)
    {
        var existing = MockPolicies.FirstOrDefault(p => p.Id == id);
        if (existing is null)
            return Results.NotFound(new ErrorResponse("NOT_FOUND", $"Política '{id}' no encontrada.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        // Mock: devuelve el DTO actualizado sin persistir nada.
        var updated = existing with
        {
            Name = request.Name,
            Type = request.Type,
            Config = request.Config,
            Weight = request.Weight,
            IsActive = request.IsActive
        };

        return Results.Ok(updated);
    }

    private static IResult Delete(Guid id)
    {
        var existing = MockPolicies.FirstOrDefault(p => p.Id == id);
        if (existing is null)
            return Results.NotFound(new ErrorResponse("NOT_FOUND", $"Política '{id}' no encontrada.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        // Mock: 204 No Content — soft-delete simulado.
        return Results.NoContent();
    }
}
