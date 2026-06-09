using System.Text.Json;
using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// AI-learned behaviour profile for a user, in a strict 1:1 relationship with <see cref="User"/>.
/// <see cref="IsColdStart"/> remains true until <see cref="AccessCount"/> reaches the
/// configurable threshold N, after which the anomaly-detection layer operates at full capacity.
/// </summary>
public class UserBehaviorProfile
{
    public UserBehaviorProfileId Id { get; init; }

    // ── Foreign Key (UNIQUE → 1:1 with User) ─────────────────────────────────

    public UserId UserId { get; init; }

    // ── ML Feature Fields ─────────────────────────────────────────────────────

    /// <summary>
    /// Learned feature vector stored as JSONB
    /// (e.g., hour sin/cos encoding, access frequency, diversity score).
    /// Null until the model has been trained at least once.
    /// </summary>
    public JsonDocument? FeatureVector { get; set; }

    /// <summary>Total number of evaluated accesses accumulated for this user.</summary>
    public int AccessCount { get; set; }

    /// <summary>True while <see cref="AccessCount"/> is below the cold-start threshold N.</summary>
    public bool IsColdStart { get; set; } = true;

    /// <summary>
    /// Current risk penalty applied when the profile is still in cold-start
    /// (decreases as <see cref="AccessCount"/> grows).
    /// </summary>
    public decimal BaseRiskPenalty { get; set; }

    /// <summary>Timestamp of the last ML.NET model retrain for this user.</summary>
    public DateTimeOffset? LastTrainedAt { get; set; }

    // ── Navigation Properties ─────────────────────────────────────────────────

    public User? User { get; set; }
}
