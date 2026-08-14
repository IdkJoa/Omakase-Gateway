using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Represents a refresh token issued to a user session/device.
/// Refresh tokens persist in PostgreSQL for 7 days and survive system restarts,
/// unlike access-token blacklist entries which live in Redis.
/// <para>
/// The token value is <strong>never stored in plain text</strong>; only its hash is persisted.
/// Rotating a refresh token revokes the previous one (<see cref="IsRevoked"/>).
/// </para>
/// </summary>
public class RefreshToken
{
    public RefreshTokenId Id { get; init; }
    public UserId UserId { get; init; }

    /// <summary>Hash only; the raw token is never persisted.</summary>
    public string TokenHash { get; init; } = string.Empty;
    public string? DeviceInfo { get; set; }
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Set after rotation or logout; a revoked token must never be accepted.</summary>
    public bool IsRevoked { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public User? User { get; set; }
}
