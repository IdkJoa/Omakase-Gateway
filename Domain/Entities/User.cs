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

    /// <summary>
    /// Secreto TOTP (RFC 6238) cifrado en reposo. Solo CLIENT_USER interactivos;
    /// null para administradores y cuentas no enroladas (SRS §7.1 / HU-046 T-102).
    /// </summary>
    public string? TotpSecret { get; set; }

    /// <summary>Indica si el step-up TOTP está activo para la cuenta (HU-046 T-102).</summary>
    public bool MfaEnabled { get; set; }

    /// <summary>
    /// True si el client user es interactivo (humano). Los no interactivos (service accounts)
    /// no pueden completar el step-up: su veredicto Challenge escala a Block (HU-046 T-106).
    /// </summary>
    public bool IsInteractive { get; set; } = true;

    // Navigation Properties
    public UserBehaviorProfile? BehaviorProfile { get; set; }
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
    public ICollection<AccessPolicy> CreatedPolicies { get; set; } = new List<AccessPolicy>();
}