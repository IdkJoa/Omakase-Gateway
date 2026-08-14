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
    public Guid EvaluationId { get; init; }

    // Nullable: actor may be unidentifiable (e.g. request rejected before identity resolution).
    public UserId? UserId { get; set; }
    public ProtectedServiceId? ServiceId { get; set; }

    public string SourceIp { get; init; } = string.Empty;
    public JsonDocument? Geo { get; set; }
    public string? UserAgent { get; set; }
    public string? FingerprintHash { get; set; }

    public decimal PolicyScore { get; init; }
    public decimal AnomalyScore { get; init; }
    public decimal RiskScore { get; init; }
    public Verdict Verdict { get; init; }

    /// <summary>JSONB, not a join table: a GIN index answers rule-lookup queries without the write-amplification a join table would add at this volume.</summary>
    public JsonDocument? TriggeredRules { get; set; }

    public DateTimeOffset EvaluatedAt { get; init; } = DateTimeOffset.UtcNow;

    public User? User { get; set; }
    public ProtectedService? ProtectedService { get; set; }
}
