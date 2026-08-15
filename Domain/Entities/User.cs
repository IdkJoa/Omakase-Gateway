using Domain.Common;
using Domain.ValueObjects;

namespace Domain.Entities;

public class User : IAuditableEntity
{
    public UserId Id { get; init; }
    public string Username { get; init; }
    public UserType Type { get; set; }
    public string PasswordHash { get; init; }
    public string KeycloakSub { get; set; }
    public bool IsActive { get; set; } = true;
    public int FailedAttempts { get; set; } = 0;
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>TOTP cifrado en reposo; solo CLIENT_USER interactivos, null en el resto (HU-046 T-102).</summary>
    public string? TotpSecret { get; set; }

    public bool MfaEnabled { get; set; }

    /// <summary>Service accounts (no interactivas) no pueden completar el step-up TOTP: su Challenge escala a Block (HU-046 T-106).</summary>
    public bool IsInteractive { get; set; } = true;

    public UserBehaviorProfile? BehaviorProfile { get; set; }
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
    public ICollection<AccessPolicy> CreatedPolicies { get; set; } = new List<AccessPolicy>();
}