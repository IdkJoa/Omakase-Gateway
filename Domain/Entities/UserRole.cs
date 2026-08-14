using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>Resolves the many-to-many RBAC relationship between <see cref="User"/> and <see cref="Role"/>; (<see cref="UserId"/>, <see cref="RoleId"/>) must be unique.</summary>
public class UserRole
{
    public UserRoleId Id { get; init; }
    public UserId UserId { get; init; }
    public RoleId RoleId { get; init; }
    public DateTimeOffset AssignedAt { get; init; } = DateTimeOffset.UtcNow;

    public User? User { get; set; }
    public Role? Role { get; set; }
}
