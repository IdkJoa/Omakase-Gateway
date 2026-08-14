using Domain.Common;
using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Normalised RBAC role catalogue.
/// Replaces the former <c>users.role</c> text column, turning roles into
/// manageable entities with their own metadata.
/// New roles can be added, renamed, or described without altering the <c>users</c> table.
/// </summary>
public class Role : IAuditableEntity
{
    public RoleId Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Soft-delete flag; inactive roles cannot be assigned to users.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
