using Domain.Common;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OG.Dashboard.Features.Roles;

public sealed class RolesHandler(OmakaseDbContext context, ILogger<RolesHandler> logger)
{
    public async Task<Result<List<Role>>> GetRolesAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Cargando catálogo completo de roles.");
        try
        {
            var roles = await context.Roles
                .Include(r => r.UserRoles)
                .AsNoTracking()
                .ToListAsync(ct);

            return Result.Success(roles);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error inesperado al cargar los roles.");
            return Result.Failure<List<Role>>(new Error("UnhandledException", $"Ocurrió un error inesperado al cargar los roles: {e.Message}"));
        }
    }

    public async Task<Result<Role>> GetRoleByIdAsync(Guid id, CancellationToken ct = default)
    {
        logger.LogInformation("Obteniendo rol por ID: {Id}", id);
        try
        {
            var roleId = RoleId.From(id);
            var role = await context.Roles
                .Include(r => r.UserRoles)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == roleId, ct);

            if (role is null)
            {
                logger.LogWarning("No se encontró el rol con ID: {Id}", id);
                return Result.Failure<Role>(new Error("Roles.NotFound", $"Rol '{id}' no encontrado."));
            }

            return Result.Success(role);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error inesperado al obtener el rol {Id}.", id);
            return Result.Failure<Role>(new Error("UnhandledException", $"Ocurrió un error inesperado: {e.Message}"));
        }
    }

    public async Task<Result<Role>> CreateRoleAsync(string name, string? description, CancellationToken ct = default)
    {
        logger.LogInformation("Creando nuevo rol: {Name}", name);
        try
        {
            var nameUpper = name.Trim().ToUpperInvariant();
            var exists = await context.Roles.AnyAsync(r => r.Name.ToUpper() == nameUpper, ct);
            if (exists)
            {
                logger.LogWarning("Conflicto al crear rol: Ya existe un rol con el nombre {Name}", name);
                return Result.Failure<Role>(new Error("Roles.Conflict", $"Ya existe un rol con el nombre '{name}'."));
            }

            var role = new Role
            {
                Id = RoleId.New(),
                Name = name.Trim(),
                Description = description?.Trim(),
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };

            context.Roles.Add(role);
            await context.SaveChangesAsync(ct);
            logger.LogInformation("Rol creado con éxito en BD: {Id}", role.Id.Value);

            return Result.Success(role);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error inesperado al crear el rol: {Name}", name);
            return Result.Failure<Role>(new Error("UnhandledException", $"Ocurrió un error inesperado al crear el rol: {e.Message}"));
        }
    }

    public async Task<Result> UpdateRoleAsync(Guid id, string name, string? description, CancellationToken ct = default)
    {
        logger.LogInformation("Actualizando rol con ID: {Id} -> Nuevo Nombre: {Name}", id, name);
        try
        {
            var roleId = RoleId.From(id);
            var role = await context.Roles.FirstOrDefaultAsync(r => r.Id == roleId, ct);

            if (role is null)
            {
                logger.LogWarning("Intento de actualización fallido: No existe el rol {Id}", id);
                return Result.Failure(new Error("Roles.NotFound", $"Rol '{id}' no encontrado."));
            }

            var nameUpper = name.Trim().ToUpperInvariant();
            var exists = await context.Roles.AnyAsync(r => r.Name.ToUpper() == nameUpper && r.Id != roleId, ct);
            if (exists)
            {
                logger.LogWarning("Conflicto al actualizar rol: Ya existe otro rol con el nombre {Name}", name);
                return Result.Failure(new Error("Roles.Conflict", $"Ya existe otro rol con el nombre '{name}'."));
            }

            role.Name = name.Trim();
            role.Description = description?.Trim();

            context.Roles.Update(role);
            await context.SaveChangesAsync(ct);
            logger.LogInformation("Rol {Id} actualizado con éxito.", id);

            return Result.Success();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error inesperado al actualizar el rol {Id}.", id);
            return Result.Failure(new Error("UnhandledException", $"Ocurrió un error inesperado al actualizar el rol: {e.Message}"));
        }
    }

    public async Task<Result> DeleteRoleAsync(Guid id, CancellationToken ct = default)
    {
        logger.LogInformation("Eliminando rol con ID: {Id}", id);
        try
        {
            var roleId = RoleId.From(id);
            var role = await context.Roles
                .Include(r => r.UserRoles)
                .FirstOrDefaultAsync(r => r.Id == roleId, ct);

            if (role is null)
            {
                logger.LogWarning("Intento de eliminación fallido: No existe el rol {Id}", id);
                return Result.Failure(new Error("Roles.NotFound", $"Rol '{id}' no encontrado."));
            }

            if (role.UserRoles.Any())
            {
                logger.LogWarning("Intento de eliminación bloqueado: El rol {Name} ({Id}) tiene usuarios asignados.", role.Name, id);
                return Result.Failure(new Error("Roles.Conflict", $"El rol '{role.Name}' tiene usuarios asignados. Revoque los accesos antes de eliminar."));
            }

            context.Roles.Remove(role);
            await context.SaveChangesAsync(ct);
            logger.LogInformation("Rol {Id} eliminado de la base de datos.", id);

            return Result.Success();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error inesperado al eliminar el rol {Id}.", id);
            return Result.Failure(new Error("UnhandledException", $"Ocurrió un error inesperado al eliminar el rol: {e.Message}"));
        }
    }

    public async Task<Result<List<Role>>> GetRolesForUserAsync(Guid userId, CancellationToken ct = default)
    {
        logger.LogInformation("Obteniendo roles asignados al usuario: {UserId}", userId);
        try
        {
            var typedUserId = UserId.From(userId);
            var userExists = await context.Users.AnyAsync(u => u.Id == typedUserId, ct);
            if (!userExists)
            {
                logger.LogWarning("Intento de listar roles fallido: Usuario {UserId} no existe.", userId);
                return Result.Failure<List<Role>>(new Error("Users.NotFound", $"Usuario '{userId}' no encontrado."));
            }

            var roles = await context.UserRoles
                .AsNoTracking()
                .Where(ur => ur.UserId == typedUserId)
                .Select(ur => ur.Role!)
                .ToListAsync(ct);

            return Result.Success(roles);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error inesperado al obtener roles del usuario {UserId}.", userId);
            return Result.Failure<List<Role>>(new Error("UnhandledException", $"Ocurrió un error inesperado: {e.Message}"));
        }
    }

    public async Task<Result> AssignRoleToUserAsync(Guid userId, Guid roleId, CancellationToken ct = default)
    {
        logger.LogInformation("Asignando rol {RoleId} al usuario {UserId}", roleId, userId);
        try
        {
            var typedUserId = UserId.From(userId);
            var userExists = await context.Users.AnyAsync(u => u.Id == typedUserId, ct);
            if (!userExists)
            {
                logger.LogWarning("Asignación fallida: Usuario {UserId} no encontrado.", userId);
                return Result.Failure(new Error("Users.NotFound", $"Usuario '{userId}' no encontrado."));
            }

            var typedRoleId = RoleId.From(roleId);
            var role = await context.Roles.FirstOrDefaultAsync(r => r.Id == typedRoleId, ct);
            if (role is null)
            {
                logger.LogWarning("Asignación fallida: Rol {RoleId} no encontrado.", roleId);
                return Result.Failure(new Error("Roles.NotFound", $"Rol '{roleId}' no encontrado."));
            }

            if (!role.IsActive)
            {
                logger.LogWarning("Asignación fallida: Rol {Name} ({RoleId}) está inactivo.", role.Name, roleId);
                return Result.Failure(new Error("Roles.Inactive", $"El rol '{role.Name}' no está activo."));
            }

            var exists = await context.UserRoles.AnyAsync(ur => ur.UserId == typedUserId && ur.RoleId == typedRoleId, ct);
            if (exists)
            {
                logger.LogWarning("Asignación duplicada: Usuario {UserId} ya posee el rol {Name}.", userId, role.Name);
                return Result.Failure(new Error("Roles.Conflict", $"El usuario ya tiene asignado el rol '{role.Name}'."));
            }

            var userRole = new UserRole
            {
                Id = UserRoleId.New(),
                UserId = typedUserId,
                RoleId = typedRoleId,
                AssignedAt = DateTimeOffset.UtcNow
            };

            context.UserRoles.Add(userRole);
            await context.SaveChangesAsync(ct);
            logger.LogInformation("Rol {Name} asignado correctamente a usuario {UserId}.", role.Name, userId);

            return Result.Success();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error inesperado al asignar rol {RoleId} a usuario {UserId}.", roleId, userId);
            return Result.Failure(new Error("UnhandledException", $"Ocurrió un error inesperado al asignar el rol: {e.Message}"));
        }
    }

    public async Task<Result> RevokeRoleFromUserAsync(Guid userId, Guid roleId, CancellationToken ct = default)
    {
        logger.LogInformation("Revocando rol {RoleId} del usuario {UserId}", roleId, userId);
        try
        {
            var typedUserId = UserId.From(userId);
            var typedRoleId = RoleId.From(roleId);

            var userRole = await context.UserRoles
                .FirstOrDefaultAsync(ur => ur.UserId == typedUserId && ur.RoleId == typedRoleId, ct);

            if (userRole is null)
            {
                logger.LogWarning("Revocación fallida: No se encontró relación para el usuario {UserId} y rol {RoleId}", userId, roleId);
                return Result.Failure(new Error("Roles.NotFound", "Asignación de rol no encontrada."));
            }

            context.UserRoles.Remove(userRole);
            await context.SaveChangesAsync(ct);
            logger.LogInformation("Rol {RoleId} revocado del usuario {UserId} con éxito.", roleId, userId);

            return Result.Success();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error inesperado al revocar rol {RoleId} del usuario {UserId}.", roleId, userId);
            return Result.Failure(new Error("UnhandledException", $"Ocurrió un error inesperado al revocar el rol: {e.Message}"));
        }
    }
}
