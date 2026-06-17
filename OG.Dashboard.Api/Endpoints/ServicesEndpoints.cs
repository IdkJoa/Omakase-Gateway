using OG.Dashboard.Api.Contracts.Common;
using OG.Dashboard.Api.Contracts.Services;

namespace OG.Dashboard.Api.Endpoints;

/// <summary>
/// Endpoints mock CRUD de servicios protegidos por el Gateway.
/// CRUD bajo /api/v1/services — corresponde a la entidad ProtectedService del dominio.
/// El campo <c>Name</c> de cada servicio actúa como clusterId en YARP.
/// </summary>
public static class ServicesEndpoints
{
    // ── Datos mock ────────────────────────────────────────────────────────────

    private static readonly List<ProtectedServiceDto> MockServices =
    [
        new(
            Id: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Name: "orders-service",
            UpstreamUrl: "https://orders-svc:8080",
            RequiresAuth: true,
            IsActive: true,
            CreatedAt: new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
            AssociatedPoliciesCount: 3
        ),
        new(
            Id: Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff"),
            Name: "payments-service",
            UpstreamUrl: "https://payments-svc:8081",
            RequiresAuth: true,
            IsActive: true,
            CreatedAt: new DateTimeOffset(2026, 5, 1, 9, 5, 0, TimeSpan.Zero),
            AssociatedPoliciesCount: 4
        ),
        new(
            Id: Guid.Parse("cccccccc-dddd-eeee-ffff-aaaaaaaaaaaa"),
            Name: "inventory-service",
            UpstreamUrl: "https://inventory-svc:8082",
            RequiresAuth: false,
            IsActive: true,
            CreatedAt: new DateTimeOffset(2026, 5, 2, 10, 0, 0, TimeSpan.Zero),
            AssociatedPoliciesCount: 1
        ),
        new(
            Id: Guid.Parse("dddddddd-eeee-ffff-aaaa-bbbbbbbbbbbb"),
            Name: "legacy-api",
            UpstreamUrl: "http://legacy-api:3000",
            RequiresAuth: false,
            IsActive: false,
            CreatedAt: new DateTimeOffset(2026, 4, 15, 8, 0, 0, TimeSpan.Zero),
            AssociatedPoliciesCount: 0
        ),
    ];

    // ── Registro de endpoints ─────────────────────────────────────────────────

    public static IEndpointRouteBuilder MapServicesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/services")
            .WithTags("Protected Services")
            .WithOpenApi();

        // GET /api/v1/services
        group.MapGet("/", GetAll)
            .WithName("GetServices")
            .WithSummary("Listar servicios protegidos")
            .WithDescription("Devuelve la lista paginada de servicios protegidos por el Gateway. Soporta filtro por isActive.");

        // GET /api/v1/services/{id}
        group.MapGet("/{id:guid}", GetById)
            .WithName("GetServiceById")
            .WithSummary("Obtener un servicio protegido por ID");

        // POST /api/v1/services
        group.MapPost("/", Create)
            .WithName("CreateService")
            .WithSummary("Registrar un nuevo servicio protegido")
            .Produces<ProtectedServiceDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        // PUT /api/v1/services/{id}
        group.MapPut("/{id:guid}", Update)
            .WithName("UpdateService")
            .WithSummary("Actualizar un servicio protegido existente")
            .Produces<ProtectedServiceDto>(StatusCodes.Status200OK);

        // DELETE /api/v1/services/{id}
        group.MapDelete("/{id:guid}", Delete)
            .WithName("DeleteService")
            .WithSummary("Eliminar (soft-delete) un servicio protegido")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private static IResult GetAll(int page = 1, int pageSize = 25, bool? isActive = null)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 100) pageSize = 25;

        var filtered = MockServices.AsEnumerable();
        if (isActive.HasValue)
            filtered = filtered.Where(s => s.IsActive == isActive.Value);

        var list = filtered.ToList();
        var data = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Results.Ok(new PagedResponse<ProtectedServiceDto>(page, pageSize, list.Count, data));
    }

    private static IResult GetById(Guid id)
    {
        var service = MockServices.FirstOrDefault(s => s.Id == id);
        if (service is null)
            return Results.NotFound(new ErrorResponse("NOT_FOUND", $"Servicio '{id}' no encontrado.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        return Results.Ok(service);
    }

    private static IResult Create(UpsertServiceRequest request)
    {
        // Verificar nombre duplicado (mock)
        if (MockServices.Any(s => s.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase)))
            return Results.Conflict(new ErrorResponse("CONFLICT",
                $"Ya existe un servicio con el nombre '{request.Name}'.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        var created = new ProtectedServiceDto(
            Id: Guid.NewGuid(),
            Name: request.Name,
            UpstreamUrl: request.UpstreamUrl,
            RequiresAuth: request.RequiresAuth,
            IsActive: request.IsActive,
            CreatedAt: DateTimeOffset.UtcNow,
            AssociatedPoliciesCount: 0
        );

        return Results.Created($"/api/v1/services/{created.Id}", created);
    }

    private static IResult Update(Guid id, UpsertServiceRequest request)
    {
        var existing = MockServices.FirstOrDefault(s => s.Id == id);
        if (existing is null)
            return Results.NotFound(new ErrorResponse("NOT_FOUND", $"Servicio '{id}' no encontrado.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        var updated = existing with
        {
            Name = request.Name,
            UpstreamUrl = request.UpstreamUrl,
            RequiresAuth = request.RequiresAuth,
            IsActive = request.IsActive
        };

        return Results.Ok(updated);
    }

    private static IResult Delete(Guid id)
    {
        var existing = MockServices.FirstOrDefault(s => s.Id == id);
        if (existing is null)
            return Results.NotFound(new ErrorResponse("NOT_FOUND", $"Servicio '{id}' no encontrado.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        return Results.NoContent();
    }
}
