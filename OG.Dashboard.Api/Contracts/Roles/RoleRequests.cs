using System.ComponentModel.DataAnnotations;

namespace OG.Dashboard.Api.Contracts.Roles;

/// <summary>
/// Payload para crear un nuevo rol.
/// Usado en POST /api/v1/roles.
/// </summary>
public sealed record CreateRoleRequest(
    [Required, MinLength(2), MaxLength(50)]
    string Name,

    [MaxLength(200)]
    string? Description
);

/// <summary>
/// Payload para asignar un rol existente a un usuario.
/// Usado en POST /api/v1/roles/{roleId}/users.
/// </summary>
public sealed record AssignRoleRequest(
    /// <summary>ID del usuario al que se le asignará el rol.</summary>
    [Required]
    Guid UserId
);

/// <summary>
/// Payload para revocar el rol de un usuario.
/// Usado en DELETE /api/v1/roles/{roleId}/users/{userId}.
/// </summary>
public sealed record RevokeRoleRequest(
    Guid RoleId,
    Guid UserId
);
