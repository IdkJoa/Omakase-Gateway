using OG.Dashboard.Api.Contracts.Common;
using OG.Dashboard.Api.Contracts.Roles;
using OG.Dashboard.Features.Roles;
using Application.Common.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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

    private static async Task<IResult> GetAll(
        [FromServices] RolesHandler handler,
        [FromServices] IOutputSanitizer enc,
        CancellationToken ct)
    {
        var result = await handler.GetRolesAsync(ct);
        if (result.IsFailure)
        {
            return Results.BadRequest(new ErrorResponse("ERROR", result.Error.Description, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));
        }

        var dtos = result.Value.Select(r => new RoleDto(
            r.Id.Value,
            enc.Sanitize(r.Name),
            r.Description is not null ? enc.Sanitize(r.Description) : null,
            r.CreatedAt,
            r.UserRoles.Count
        )).ToList();

        return Results.Ok(dtos);
    }

    private static async Task<IResult> GetById(
        Guid id,
        [FromServices] RolesHandler handler,
        [FromServices] IOutputSanitizer enc,
        CancellationToken ct)
    {
        var result = await handler.GetRoleByIdAsync(id, ct);
        if (result.IsFailure)
        {
            return Results.NotFound(new ErrorResponse("NOT_FOUND", result.Error.Description, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));
        }

        var r = result.Value;
        var dto = new RoleDto(
            r.Id.Value,
            enc.Sanitize(r.Name),
            r.Description is not null ? enc.Sanitize(r.Description) : null,
            r.CreatedAt,
            r.UserRoles.Count
        );

        return Results.Ok(dto);
    }

    private static async Task<IResult> Create(
        CreateRoleRequest request,
        [FromServices] RolesHandler handler,
        [FromServices] IOutputSanitizer enc,
        CancellationToken ct)
    {
        var result = await handler.CreateRoleAsync(request.Name, request.Description, ct);
        if (result.IsFailure)
        {
            if (result.Error.Code == "Roles.InvalidName")
            {
                return Results.BadRequest(new ErrorResponse("INVALID_NAME", result.Error.Description, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));
            }
            if (result.Error.Code == "Roles.Conflict")
            {
                return Results.Conflict(new ErrorResponse("CONFLICT", result.Error.Description, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));
            }
            return Results.BadRequest(new ErrorResponse("ERROR", result.Error.Description, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));
        }

        var r = result.Value;
        // Output-encoding consistente con GetAll/GetById y con los Create de policies/services:
        // se sanitiza el nombre/descripcion en la respuesta (defensa en profundidad).
        var dto = new RoleDto(
            r.Id.Value,
            enc.Sanitize(r.Name),
            r.Description is not null ? enc.Sanitize(r.Description) : null,
            r.CreatedAt,
            0
        );

        return Results.Created($"/api/v1/roles/{r.Id.Value}", dto);
    }

    private static async Task<IResult> Delete(
        Guid id,
        [FromServices] RolesHandler handler,
        CancellationToken ct)
    {
        var result = await handler.DeleteRoleAsync(id, ct);
        if (result.IsFailure)
        {
            if (result.Error.Code == "Roles.NotFound")
            {
                return Results.NotFound(new ErrorResponse("NOT_FOUND", result.Error.Description, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));
            }
            if (result.Error.Code == "Roles.Conflict")
            {
                return Results.Conflict(new ErrorResponse("CONFLICT", result.Error.Description, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));
            }
            return Results.BadRequest(new ErrorResponse("ERROR", result.Error.Description, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));
        }

        return Results.NoContent();
    }

    private static async Task<IResult> Update(
        Guid id,
        UpdateRoleRequest request,
        [FromServices] RolesHandler handler,
        CancellationToken ct)
    {
        var result = await handler.UpdateRoleAsync(id, request.Name, request.Description, ct);
        if (result.IsFailure)
        {
            if (result.Error.Code == "Roles.NotFound")
            {
                return Results.NotFound(new ErrorResponse("NOT_FOUND", result.Error.Description, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));
            }
            if (result.Error.Code == "Roles.InvalidName")
            {
                return Results.BadRequest(new ErrorResponse("INVALID_NAME", result.Error.Description, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));
            }
            if (result.Error.Code == "Roles.Conflict")
            {
                return Results.Conflict(new ErrorResponse("CONFLICT", result.Error.Description, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));
            }
            return Results.BadRequest(new ErrorResponse("ERROR", result.Error.Description, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));
        }

        return Results.NoContent();
    }

    private static async Task<IResult> GetRolesForUser(
        Guid userId,
        [FromServices] RolesHandler handler,
        [FromServices] IOutputSanitizer enc,
        CancellationToken ct)
    {
        var result = await handler.GetRolesForUserAsync(userId, ct);
        if (result.IsFailure)
        {
            return Results.NotFound(new ErrorResponse("NOT_FOUND", result.Error.Description, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));
        }

        var dtos = result.Value.Select(r => new RoleDto(
            r.Id.Value,
            enc.Sanitize(r.Name),
            r.Description is not null ? enc.Sanitize(r.Description) : null,
            r.CreatedAt,
            r.UserRoles.Count
        )).ToList();

        return Results.Ok(dtos);
    }

    private static async Task<IResult> AssignRoleToUser(
        Guid userId,
        AssignRoleToUserRequest request,
        [FromServices] RolesHandler handler,
        CancellationToken ct)
    {
        var result = await handler.AssignRoleToUserAsync(userId, request.RoleId, ct);
        if (result.IsFailure)
        {
            if (result.Error.Code.Contains("NotFound"))
            {
                return Results.NotFound(new ErrorResponse("NOT_FOUND", result.Error.Description, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));
            }
            if (result.Error.Code == "Roles.Conflict")
            {
                return Results.Conflict(new ErrorResponse("CONFLICT", result.Error.Description, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));
            }
            return Results.BadRequest(new ErrorResponse("BAD_REQUEST", result.Error.Description, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));
        }

        return Results.NoContent();
    }

    private static async Task<IResult> RevokeRoleFromUser(
        Guid userId,
        Guid roleId,
        [FromServices] RolesHandler handler,
        CancellationToken ct)
    {
        var result = await handler.RevokeRoleFromUserAsync(userId, roleId, ct);
        if (result.IsFailure)
        {
            return Results.NotFound(new ErrorResponse("NOT_FOUND", result.Error.Description, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));
        }

        return Results.NoContent();
    }
}
