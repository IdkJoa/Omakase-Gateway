namespace OG.Dashboard.Api.Contracts.Roles;

/// <summary>
/// Representa un rol del sistema RBAC tal como se expone en la API administrativa.
/// Los roles iniciales son Viewer (solo lectura) y Admin (gestión completa);
/// el catálogo es extensible sin modificar el esquema.
/// </summary>
public sealed record RoleDto(
    Guid Id,

    /// <summary>Nombre del rol (ej. Viewer, Admin).</summary>
    string Name,

    /// <summary>Descripción del rol y sus permisos.</summary>
    string? Description,

    DateTimeOffset CreatedAt,

    /// <summary>Número de usuarios asignados a este rol.</summary>
    int UsersCount
);
