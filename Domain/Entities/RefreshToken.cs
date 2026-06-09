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

    // ── Foreign Key ───────────────────────────────────────────────────────────

    /// <summary>Owner of this token. CASCADE on user deletion.</summary>
    public UserId UserId { get; init; }

    // ── Token Data ────────────────────────────────────────────────────────────

    /// <summary>Bcrypt/SHA-256 hash of the refresh token (never stored in clear).</summary>
    public string TokenHash { get; init; } = string.Empty;

    /// <summary>Device or session metadata provided at login (optional).</summary>
    public string? DeviceInfo { get; set; }

    /// <summary>Expiration timestamp (issued_at + 7 days).</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Set to true after rotation or explicit logout.
    /// A revoked token must never be accepted.
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>Timestamp at which the token was issued.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    // ── Navigation Properties ─────────────────────────────────────────────────

    public User? User { get; set; }
}
