using System.Text.Json;
using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Immutable, append-only record of every risk evaluation performed by the Gateway.
/// This is the highest-volume entity in the system.
/// <para>
/// <see cref="Geo"/> and <see cref="TriggeredRules"/> are deliberately stored as JSONB
/// (denormalised) to avoid the write-amplification that a join table would introduce at scale.
/// A GIN index on <see cref="TriggeredRules"/> enables efficient queries.
/// </para>
/// </summary>
public class AuditLog
{
    public AuditLogId Id { get; init; }

    /// <summary>
    /// Stable identifier for this evaluation, exposed on the API surface.
    /// </summary>
    public Guid EvaluationId { get; init; }

    // ── Foreign Keys (both nullable: actor may be unidentifiable) ─────────────

    /// <summary>User who triggered the request; null when identity could not be resolved.</summary>
    public UserId? UserId { get; set; }

    /// <summary>Destination protected service; null for unregistered upstream targets.</summary>
    public ProtectedServiceId? ServiceId { get; set; }

    // ── Request Context ───────────────────────────────────────────────────────

    /// <summary>Source IP address (IPv4 or IPv6, max 45 chars).</summary>
    public string SourceIp { get; init; } = string.Empty;

    /// <summary>Geographic resolution of the source IP (country, city, coordinates) as JSONB.</summary>
    public JsonDocument? Geo { get; set; }

    /// <summary>Sanitised User-Agent string.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Hash of the browser/device fingerprint.</summary>
    public string? FingerprintHash { get; set; }

    // ── Risk Scores ───────────────────────────────────────────────────────────

    /// <summary>Score produced by the deterministic policy layer (0–100).</summary>
    public decimal PolicyScore { get; init; }

    /// <summary>Score produced by the AI/anomaly-detection layer (0–100).</summary>
    public decimal AnomalyScore { get; init; }

    /// <summary>Consolidated final risk score (0–100).</summary>
    public decimal RiskScore { get; init; }

    /// <summary>Decision rendered by the evaluation engine.</summary>
    public Verdict Verdict { get; init; }

    /// <summary>
    /// Array of triggered policy rules stored as JSONB (denormalised).
    /// Queried via GIN index; intentionally not a join table for write-performance reasons.
    /// </summary>
    public JsonDocument? TriggeredRules { get; set; }

    /// <summary>Timestamp at which the evaluation occurred.</summary>
    public DateTimeOffset EvaluatedAt { get; init; } = DateTimeOffset.UtcNow;

    // ── Navigation Properties ─────────────────────────────────────────────────

    public User? User { get; set; }
    public ProtectedService? ProtectedService { get; set; }
}
