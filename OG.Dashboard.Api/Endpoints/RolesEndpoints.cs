using OG.Dashboard.Api.Contracts.Common;
using OG.Dashboard.Api.Contracts.Roles;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Domain.Entities;
using Domain.ValueObjects;
using Application.Common.Security;

namespace OG.Dashboard.Api.Endpoints;

/// <summary>
/// Endpoints CRUD del catálogo RBAC de roles y asignación a usuarios.
/// CRUD bajo /api/v1/roles e /api/v1/users/{userId}/roles.
/// </summary>
public static class RolesEndpoints
{
    public static IEndpointRouteBuilder MapRolesEndpoints(this IEndpointRouteBuilder app)
    {
        var rolesGroup = app
            .MapGroup("/api/v1/roles")
            .WithTags("Roles")
            .WithOpenApi();

        // GET /api/v1/roles
        rolesGroup.MapGet("/", GetAll)
            .RequireAuthorization("ReadAccess")
            .WithName("GetRoles")
            .WithSummary("Listar roles del sistema")
            .WithDescription("Devuelve el catálogo RBAC de roles con número de usuarios asignados.");

        // GET /api/v1/roles/{id}
        rolesGroup.MapGet("/{id:guid}", GetById)
            .RequireAuthorization("ReadAccess")
            .WithName("GetRoleById")
            .WithSummary("Obtener un rol por ID");

        // POST /api/v1/roles
        rolesGroup.MapPost("/", Create)
            .RequireAuthorization("AdminOnly")
            .WithName("CreateRole")
            .WithSummary("Crear un nuevo rol")
            .Produces<RoleDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        // DELETE /api/v1/roles/{id}
        rolesGroup.MapDelete("/{id:guid}", Delete)
            .RequireAuthorization("AdminOnly")
            .WithName("DeleteRole")
            .WithSummary("Eliminar un rol (solo si no tiene usuarios asignados)")
            .Produces(StatusCodes.Status204NoContent);

        // PUT /api/v1/roles/{id}
        rolesGroup.MapPut("/{id:guid}", Update)
            .RequireAuthorization("AdminOnly")
            .WithName("UpdateRole")
            .WithSummary("Actualizar un rol existente")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        // Endpoints de asignación bajo /api/v1/users/{userId}/roles (T-056)
        var userRolesGroup = app
            .MapGroup("/api/v1/users/{userId:guid}/roles")
            .WithTags("User Roles")
            .WithOpenApi();

        // GET /api/v1/users/{userId}/roles
        userRolesGroup.MapGet("/", GetRolesForUser)
            .RequireAuthorization("ReadAccess")
            .WithName("GetRolesForUser")
            .WithSummary("Listar roles de un usuario")
            .WithDescription("Devuelve los roles asignados a un usuario específico.");

        // POST /api/v1/users/{userId}/roles
        userRolesGroup.MapPost("/", AssignRoleToUser)
            .RequireAuthorization("AdminOnly")
            .WithName("AssignRoleToUser")
            .WithSummary("Asignar un rol a un usuario")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        // DELETE /api/v1/users/{userId}/roles/{roleId}
        userRolesGroup.MapDelete("/{roleId:guid}", RevokeRoleFromUser)
            .RequireAuthorization("AdminOnly")
            .WithName("RevokeRoleFromUser")
            .WithSummary("Revocar un rol de un usuario")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }

    private static async Task<IResult> GetAll(OmakaseDbContext db, IOutputSanitizer enc)
    {
        var roles = await db.Roles
            .AsNoTracking()
            .Select(r => new RoleDto(
                r.Id.Value,
                r.Name,
                r.Description,
                r.CreatedAt,
                r.UserRoles.Count
            ))
            .ToListAsync();

        // T-060: Output encoding para prevención de XSS en campos de origen externo.
        var encoded = roles.Select(r => r with
        {
            Name = enc.Sanitize(r.Name),
            Description = r.Description is not null ? enc.Sanitize(r.Description) : null
        }).ToList();

        return Results.Ok(encoded);
    }

    private static async Task<IResult> GetById(Guid id, OmakaseDbContext db, IOutputSanitizer enc)
    {
        var roleId = RoleId.From(id);
        var role = await db.Roles
            .AsNoTracking()
            .Where(r => r.Id == roleId)
            .Select(r => new RoleDto(
                r.Id.Value,
                r.Name,
                r.Description,
                r.CreatedAt,
                r.UserRoles.Count
            ))
            .FirstOrDefaultAsync();

        if (role is null)
            return Results.NotFound(new ErrorResponse("NOT_FOUND", $"Rol '{id}' no encontrado.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        var encoded = role with
        {
            Name = enc.Sanitize(role.Name),
            Description = role.Description is not null ? enc.Sanitize(role.Description) : null
        };

        return Results.Ok(encoded);
    }

    private static async Task<IResult> Create(CreateRoleRequest request, OmakaseDbContext db)
    {
        var nameUpper = request.Name.Trim().ToUpperInvariant();
        var exists = await db.Roles.AnyAsync(r => r.Name.ToUpper() == nameUpper);
        if (exists)
            return Results.Conflict(new ErrorResponse("CONFLICT",
                $"Ya existe un rol con el nombre '{request.Name}'.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        var role = new Role
        {
            Id = RoleId.New(),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var dto = new RoleDto(
            role.Id.Value,
            role.Name,
            role.Description,
            role.CreatedAt,
            0
        );

        return Results.Created($"/api/v1/roles/{role.Id.Value}", dto);
    }

    private static async Task<IResult> Delete(Guid id, OmakaseDbContext db)
    {
        var roleId = RoleId.From(id);
        var role = await db.Roles
            .Include(r => r.UserRoles)
            .FirstOrDefaultAsync(r => r.Id == roleId);

        if (role is null)
            return Results.NotFound(new ErrorResponse("NOT_FOUND", $"Rol '{id}' no encontrado.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        if (role.UserRoles.Any())
            return Results.Conflict(new ErrorResponse("CONFLICT",
                $"El rol '{role.Name}' tiene usuarios asignados. Revoque los accesos antes de eliminar.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        db.Roles.Remove(role);
        await db.SaveChangesAsync();

        return Results.NoContent();
    }

    private static async Task<IResult> Update(Guid id, UpdateRoleRequest request, OmakaseDbContext db)
    {
        var roleId = RoleId.From(id);
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == roleId);

        if (role is null)
            return Results.NotFound(new ErrorResponse("NOT_FOUND", $"Rol '{id}' no encontrado.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        var nameUpper = request.Name.Trim().ToUpperInvariant();
        var exists = await db.Roles.AnyAsync(r => r.Name.ToUpper() == nameUpper && r.Id != roleId);
        if (exists)
            return Results.Conflict(new ErrorResponse("CONFLICT",
                $"Ya existe otro rol con el nombre '{request.Name}'.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        role.Name = request.Name.Trim();
        role.Description = request.Description?.Trim();

        db.Roles.Update(role);
        await db.SaveChangesAsync();

        return Results.NoContent();
    }

    private static async Task<IResult> GetRolesForUser(Guid userId, OmakaseDbContext db)
    {
        var typedUserId = UserId.From(userId);
        var userExists = await db.Users.AnyAsync(u => u.Id == typedUserId);
        if (!userExists)
            return Results.NotFound(new ErrorResponse("NOT_FOUND", $"Usuario '{userId}' no encontrado.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        var roles = await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == typedUserId)
            .Select(ur => new RoleDto(
                ur.Role!.Id.Value,
                ur.Role.Name,
                ur.Role.Description,
                ur.Role.CreatedAt,
                ur.Role.UserRoles.Count
            ))
            .ToListAsync();

        return Results.Ok(roles);
    }

    private static async Task<IResult> AssignRoleToUser(Guid userId, AssignRoleToUserRequest request, OmakaseDbContext db)
    {
        var typedUserId = UserId.From(userId);
        var userExists = await db.Users.AnyAsync(u => u.Id == typedUserId);
        if (!userExists)
            return Results.NotFound(new ErrorResponse("NOT_FOUND", $"Usuario '{userId}' no encontrado.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        var typedRoleId = RoleId.From(request.RoleId);
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == typedRoleId);
        if (role is null)
            return Results.NotFound(new ErrorResponse("NOT_FOUND", $"Rol '{request.RoleId}' no encontrado.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        if (!role.IsActive)
            return Results.BadRequest(new ErrorResponse("BAD_REQUEST", $"El rol '{role.Name}' no está activo.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        // Validar combinación única
        var exists = await db.UserRoles.AnyAsync(ur => ur.UserId == typedUserId && ur.RoleId == typedRoleId);
        if (exists)
            return Results.Conflict(new ErrorResponse("CONFLICT",
                $"El usuario ya tiene asignado el rol '{role.Name}'.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        var userRole = new UserRole
        {
            Id = UserRoleId.New(),
            UserId = typedUserId,
            RoleId = typedRoleId,
            AssignedAt = DateTimeOffset.UtcNow
        };

        db.UserRoles.Add(userRole);
        await db.SaveChangesAsync();

        return Results.NoContent();
    }

    private static async Task<IResult> RevokeRoleFromUser(Guid userId, Guid roleId, OmakaseDbContext db)
    {
        var typedUserId = UserId.From(userId);
        var typedRoleId = RoleId.From(roleId);

        var userRole = await db.UserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == typedUserId && ur.RoleId == typedRoleId);

        if (userRole is null)
            return Results.NotFound(new ErrorResponse("NOT_FOUND", "Asignación de rol no encontrada.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        db.UserRoles.Remove(userRole);
        await db.SaveChangesAsync();

        return Results.NoContent();
    }
}
