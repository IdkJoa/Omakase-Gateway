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

    /// <summary>
    /// Unique role name (e.g., ADMIN, VIEWER, AUDITOR).
    /// Used as the authoritative identifier in policy decisions.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable description of the role's purpose.</summary>
    public string? Description { get; set; }

    /// <summary>Soft-delete flag; inactive roles cannot be assigned to users.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Última modificación del rol (SRS §7.9). Null mientras no se edite.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    // ── Navigation Properties ─────────────────────────────────────────────────

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
