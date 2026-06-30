using Domain.Entities;

namespace Application.Common.RiskEngine.Scoring;

/// <summary>
/// Consolidates the deterministic Policy Score and the AI Anomaly Score into a
/// single Risk Score (0–100) and derives the verdict from the configured
/// thresholds (T-029 / SRS §9.1).
/// <para>
/// Threshold mapping (entity names differ from the SRS wording):
/// <list type="bullet">
///   <item><c>ChallengeThreshold</c> = allow ceiling → score ≤ it ⇒ ALLOW.</item>
///   <item><c>BlockThreshold</c> = challenge ceiling → score ≤ it ⇒ CHALLENGE, else BLOCK.</item>
/// </list>
/// </para>
/// </summary>
public sealed class RiskScoreConsolidator : IRiskScoreConsolidator
{
    public ConsolidatedRisk Consolidate(decimal policyScore, decimal anomalyScore, int accessCount, RiskScoreConfig config)
    {
        var coldStartPenalty = CalculateColdStartPenalty(accessCount, config);

        var risk = (config.PolicyWeight * policyScore)
                 + (config.AnomalyWeight * anomalyScore)
                 + coldStartPenalty;

        // Risk Score is defined on a 0–100 scale.
        risk = Math.Clamp(risk, 0m, 100m);

        var verdict = risk <= config.ChallengeThreshold ? Verdict.Allow
                    : risk <= config.BlockThreshold ? Verdict.Challenge
                    : Verdict.Block;

        return new ConsolidatedRisk(risk, verdict, coldStartPenalty);
    }

    /// <summary>
    /// Linear cold-start decay: <c>penalty × max(0, 1 − accessCount / N)</c>.
    /// Guards against N ≤ 0 to avoid division by zero.
    /// </summary>
    private static decimal CalculateColdStartPenalty(int accessCount, RiskScoreConfig config)
    {
        if (config.ColdStartN <= 0)
            return 0m;

        var factor = 1m - ((decimal)accessCount / config.ColdStartN);
        if (factor < 0m)
            factor = 0m;

        return config.ColdStartPenalty * factor;
    }
}
