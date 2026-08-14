using System.ComponentModel.DataAnnotations;

namespace OG.Dashboard.Api.Contracts.Roles;

public sealed record CreateRoleRequest(
    [Required, MinLength(2), MaxLength(50)]
    string Name,

    [MaxLength(200)]
    string? Description
);

public sealed record AssignRoleRequest(
    [Required]
    Guid UserId
);

public sealed record RevokeRoleRequest(
    Guid RoleId,
    Guid UserId
);

public sealed record AssignRoleToUserRequest(
    [Required]
    Guid RoleId
);

public sealed record UpdateRoleRequest(
    [Required, MinLength(2), MaxLength(50)]
    string Name,

    [MaxLength(200)]
    string? Description
);
