using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Global, single-row configuration for the risk-score evaluation engine.
/// Stores the weights and thresholds that balance the deterministic policy layer
/// against the AI anomaly-detection layer.
/// <para>
/// This entity has no foreign-key relationships (independent, as per the data-model document).
/// Only one row should ever exist; enforced at the application layer.
/// </para>
/// </summary>
public class RiskScoreConfig
{
    public RiskScoreConfigId Id { get; init; }

    /// <summary>
    /// Weight (0–1) applied to the deterministic Policy Score in the final risk calculation.
    /// </summary>
    public decimal PolicyWeight { get; set; }

    /// <summary>
    /// Weight (0–1) applied to the AI Anomaly Score in the final risk calculation.
    /// <c>PolicyWeight + AnomalyWeight</c> should equal 1.
    /// </summary>
    public decimal AnomalyWeight { get; set; }

    /// <summary>
    /// Base risk penalty added for users in cold-start (insufficient training data).
    /// Decreases as <c>UserBehaviorProfile.AccessCount</c> grows.
    /// </summary>
    public decimal ColdStartPenalty { get; set; }

    /// <summary>
    /// Risk score threshold above which the verdict is <see cref="Verdict.Block"/>.
    /// </summary>
    public decimal BlockThreshold { get; set; }

    /// <summary>
    /// Risk score threshold above which the verdict is <see cref="Verdict.Challenge"/>.
    /// Scores below this value result in <see cref="Verdict.Allow"/>.
    /// </summary>
    public decimal ChallengeThreshold { get; set; }

    /// <summary>Timestamp of the last configuration change.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
