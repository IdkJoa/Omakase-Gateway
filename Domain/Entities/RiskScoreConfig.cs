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

    /// <summary><c>PolicyWeight + AnomalyWeight</c> should equal 1.</summary>
    public decimal PolicyWeight { get; set; }
    public decimal AnomalyWeight { get; set; }

    /// <summary>Decays linearly to 0 as <c>UserBehaviorProfile.AccessCount</c> approaches <see cref="ColdStartN"/>.</summary>
    public decimal ColdStartPenalty { get; set; }

    /// <summary>Access-count threshold at which the cold-start penalty is fully extinguished (SRS §7.6 default: 10).</summary>
    public int ColdStartN { get; set; }

    public decimal BlockThreshold { get; set; }

    /// <summary>Scores below this result in <see cref="Verdict.Allow"/>; above, <see cref="Verdict.Block"/> takes precedence via <see cref="BlockThreshold"/>.</summary>
    public decimal ChallengeThreshold { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
