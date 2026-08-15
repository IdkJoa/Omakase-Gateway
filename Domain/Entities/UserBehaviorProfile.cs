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
    public UserId UserId { get; init; }

    /// <summary>Learned feature vector (hour sin/cos encoding, access frequency, diversity score); null until trained.</summary>
    public JsonDocument? FeatureVector { get; set; }

    public int AccessCount { get; set; }

    /// <summary>True while <see cref="AccessCount"/> is below the cold-start threshold N.</summary>
    public bool IsColdStart { get; set; } = true;

    public decimal BaseRiskPenalty { get; set; }
    public DateTimeOffset? LastTrainedAt { get; set; }

    public User? User { get; set; }
}
