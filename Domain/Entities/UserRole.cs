using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Join entity that resolves the many-to-many RBAC relationship between
/// <see cref="User"/> and <see cref="Role"/>.
/// A user may hold several roles simultaneously (e.g., Admin + Auditor),
/// and a role may be assigned to many users.
/// <para>
/// The combination (<see cref="UserId"/>, <see cref="RoleId"/>) must be unique
/// to prevent duplicate assignments.
/// </para>
/// </summary>
public class UserRole
{
    public UserRoleId Id { get; init; }

    // ── Foreign Keys (both CASCADE on deletion) ───────────────────────────────

    public UserId UserId { get; init; }
    public RoleId RoleId { get; init; }

    /// <summary>Timestamp at which the role was assigned to the user.</summary>
    public DateTimeOffset AssignedAt { get; init; } = DateTimeOffset.UtcNow;

    // ── Navigation Properties ─────────────────────────────────────────────────

    public User? User { get; set; }
    public Role? Role { get; set; }
}
