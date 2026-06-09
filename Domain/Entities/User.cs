using Domain.ValueObjects;

namespace Domain.Entities;

public class User
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

    // Navigation Properties
    public UserBehaviorProfile? BehaviorProfile { get; set; }
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
    public ICollection<AccessPolicy> CreatedPolicies { get; set; } = new List<AccessPolicy>();
}