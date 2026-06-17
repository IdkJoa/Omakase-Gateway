using OG.Dashboard.Api.Contracts.Common;
using OG.Dashboard.Api.Contracts.Roles;

namespace OG.Dashboard.Api.Endpoints;

/// <summary>
/// Endpoints mock CRUD del catálogo RBAC de roles.
/// CRUD bajo /api/v1/roles — corresponde a las entidades Role / UserRole del dominio.
/// Roles iniciales del SRS: Viewer (solo lectura) y Admin (gestión completa).
/// El catálogo es extensible sin modificar el esquema de BD.
/// </summary>
public static class RolesEndpoints
{
    // ── Datos mock ────────────────────────────────────────────────────────────

    private static readonly List<RoleDto> MockRoles =
    [
        new(
            Id: Guid.Parse("role-0001-0000-0000-0000-000000000000"),
            Name: "Admin",
            Description: "Acceso completo al Dashboard: gestión de políticas, servicios, usuarios y configuración de Risk Score.",
            CreatedAt: new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            UsersCount: 1
        ),
        new(
            Id: Guid.Parse("role-0002-0000-0000-0000-000000000000"),
            Name: "Viewer",
            Description: "Solo lectura: puede explorar logs de auditoría, métricas y configuración, pero no modificar nada.",
            CreatedAt: new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            UsersCount: 1
        ),
    ];

    // ── Registro de endpoints ─────────────────────────────────────────────────

    public static IEndpointRouteBuilder MapRolesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/roles")
            .WithTags("Roles")
            .WithOpenApi();

        // GET /api/v1/roles
        group.MapGet("/", GetAll)
            .WithName("GetRoles")
            .WithSummary("Listar roles del sistema")
            .WithDescription("Devuelve el catálogo RBAC de roles con número de usuarios asignados.");

        // GET /api/v1/roles/{id}
        group.MapGet("/{id:guid}", GetById)
            .WithName("GetRoleById")
            .WithSummary("Obtener un rol por ID");

        // POST /api/v1/roles
        group.MapPost("/", Create)
            .WithName("CreateRole")
            .WithSummary("Crear un nuevo rol")
            .Produces<RoleDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        // DELETE /api/v1/roles/{id}
        group.MapDelete("/{id:guid}", Delete)
            .WithName("DeleteRole")
            .WithSummary("Eliminar un rol (solo si no tiene usuarios asignados)")
            .Produces(StatusCodes.Status204NoContent);

        // POST /api/v1/roles/{roleId}/users — Asignar rol a un usuario
        group.MapPost("/{roleId:guid}/users", AssignToUser)
            .WithName("AssignRoleToUser")
            .WithSummary("Asignar un rol a un usuario")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        // DELETE /api/v1/roles/{roleId}/users/{userId} — Revocar rol de un usuario
        group.MapDelete("/{roleId:guid}/users/{userId:guid}", RevokeFromUser)
            .WithName("RevokeRoleFromUser")
            .WithSummary("Revocar un rol de un usuario")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private static IResult GetAll() => Results.Ok(MockRoles.AsReadOnly());

    private static IResult GetById(Guid id)
    {
        var role = MockRoles.FirstOrDefault(r => r.Id == id);
        if (role is null)
            return Results.NotFound(new ErrorResponse("NOT_FOUND", $"Rol '{id}' no encontrado.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        return Results.Ok(role);
    }

    private static IResult Create(CreateRoleRequest request)
    {
        if (MockRoles.Any(r => r.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase)))
            return Results.Conflict(new ErrorResponse("CONFLICT",
                $"Ya existe un rol con el nombre '{request.Name}'.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        var created = new RoleDto(
            Id: Guid.NewGuid(),
            Name: request.Name,
            Description: request.Description,
            CreatedAt: DateTimeOffset.UtcNow,
            UsersCount: 0
        );

        return Results.Created($"/api/v1/roles/{created.Id}", created);
    }

    private static IResult Delete(Guid id)
    {
        var role = MockRoles.FirstOrDefault(r => r.Id == id);
        if (role is null)
            return Results.NotFound(new ErrorResponse("NOT_FOUND", $"Rol '{id}' no encontrado.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        // Mock: impedir eliminación si tiene usuarios (simula la restricción de integridad)
        if (role.UsersCount > 0)
            return Results.Conflict(new ErrorResponse("CONFLICT",
                $"El rol '{role.Name}' tiene {role.UsersCount} usuario(s) asignado(s). Revoque los accesos antes de eliminar.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        return Results.NoContent();
    }

    private static IResult AssignToUser(Guid roleId, AssignRoleRequest request)
    {
        var role = MockRoles.FirstOrDefault(r => r.Id == roleId);
        if (role is null)
            return Results.NotFound(new ErrorResponse("NOT_FOUND", $"Rol '{roleId}' no encontrado.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        // Mock: 204 — asignación simulada.
        return Results.NoContent();
    }

    private static IResult RevokeFromUser(Guid roleId, Guid userId)
    {
        var role = MockRoles.FirstOrDefault(r => r.Id == roleId);
        if (role is null)
            return Results.NotFound(new ErrorResponse("NOT_FOUND", $"Rol '{roleId}' no encontrado.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        return Results.NoContent();
    }
}
