using OG.Dashboard.Api.Contracts.Common;
using OG.Dashboard.Api.Contracts.Users;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Domain.Entities;
using Domain.ValueObjects;
using Application.Common.Security;

namespace OG.Dashboard.Api.Endpoints;

/// <summary>
/// Endpoints reales de usuarios y perfiles de comportamiento respaldados por base de datos.
/// GET /api/v1/users — Vista administrativa de usuarios con roles y perfil de comportamiento.
/// </summary>
public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/users")
            .WithTags("Users")
            .WithOpenApi();

        group.MapGet("/", GetAll)
            .RequireAuthorization("ReadAccess")
            .WithName("GetUsers")
            .WithSummary("Listar usuarios del sistema")
            .WithDescription(
                "Devuelve todos los usuarios (Security Officers y Client Users) " +
                "con sus roles asignados y resumen de perfil de comportamiento. " +
                "Soporta filtro por userType e isActive.");

        group.MapGet("/{id:guid}", GetById)
            .RequireAuthorization("ReadAccess")
            .WithName("GetUserById")
            .WithSummary("Obtener detalle completo de un usuario por ID");

        return app;
    }

    private static async Task<IResult> GetAll(
        OmakaseDbContext db,
        IOutputSanitizer enc,
        int page = 1,
        int pageSize = 100,
        string? userType = null,
        bool? isActive = null)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 100) pageSize = 100;

        var query = db.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Include(u => u.BehaviorProfile)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(userType))
        {
            if (Enum.TryParse<UserType>(userType, true, out var parsedType))
            {
                query = query.Where(u => u.Type == parsedType);
            }
        }

        if (isActive.HasValue)
        {
            query = query.Where(u => u.IsActive == isActive.Value);
        }

        var total = await query.CountAsync();
        var users = await query
            .OrderBy(u => u.Username)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserDto(
                u.Id.Value,
                u.Username,
                u.Type.ToString(),
                u.IsActive,
                u.FailedAttempts,
                u.LockedUntil,
                u.CreatedAt,
                u.UpdatedAt,
                u.UserRoles.Select(ur => ur.Role!.Name).ToList(),
                u.BehaviorProfile != null ? new UserBehaviorSummaryDto(
                    u.BehaviorProfile.AccessCount,
                    null,
                    0,
                    0
                ) : null
            ))
            .ToListAsync();

        var encoded = users.Select(u => u with
        {
            Username = enc.Sanitize(u.Username)
        }).ToList();

        return Results.Ok(new PagedResponse<UserDto>(page, pageSize, total, encoded));
    }

    private static async Task<IResult> GetById(Guid id, OmakaseDbContext db, IOutputSanitizer enc)
    {
        var typedUserId = UserId.From(id);
        var user = await db.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .Include(u => u.BehaviorProfile)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == typedUserId);

        if (user is null)
            return Results.NotFound(new ErrorResponse("NOT_FOUND", $"Usuario '{id}' no encontrado.",
                System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A"));

        var dto = new UserDto(
            user.Id.Value,
            enc.Sanitize(user.Username),
            user.Type.ToString(),
            user.IsActive,
            user.FailedAttempts,
            user.LockedUntil,
            user.CreatedAt,
            user.UpdatedAt,
            user.UserRoles.Select(ur => ur.Role!.Name).ToList(),
            user.BehaviorProfile != null ? new UserBehaviorSummaryDto(
                user.BehaviorProfile.AccessCount,
                null,
                0,
                0
            ) : null
        );

        return Results.Ok(dto);
    }
}
