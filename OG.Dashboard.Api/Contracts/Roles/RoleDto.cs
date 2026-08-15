namespace OG.Dashboard.Api.Contracts.Roles;

/// <summary>Los roles iniciales son Viewer (solo lectura) y Admin (gestión completa); el catálogo es extensible sin modificar el esquema.</summary>
public sealed record RoleDto(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    int UsersCount
);
