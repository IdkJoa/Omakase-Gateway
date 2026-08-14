using Domain.Entities;

namespace Application.Common.RiskEngine.Scoring;

// Entity names don't match SRS wording: ChallengeThreshold is actually the ALLOW ceiling
// (score <= it => Allow), and BlockThreshold is the CHALLENGE ceiling (score <= it => Challenge,
// else Block).
public sealed class RiskScoreConsolidator : IRiskScoreConsolidator
{
    public ConsolidatedRisk Consolidate(decimal policyScore, decimal anomalyScore, int accessCount, RiskScoreConfig config)
    {
        var coldStartPenalty = CalculateColdStartPenalty(accessCount, config);

        var risk = (config.PolicyWeight * policyScore)
                 + (config.AnomalyWeight * anomalyScore)
                 + coldStartPenalty;

        // Risk Score is defined on a 0–100 scale.
        risk = Math.Clamp(risk, 0m, 100m); // Risk Score is defined on a 0-100 scale.

        var verdict = risk <= config.ChallengeThreshold ? Verdict.Allow
                    : risk <= config.BlockThreshold ? Verdict.Challenge
                    : Verdict.Block;

        return new ConsolidatedRisk(risk, verdict, coldStartPenalty);
    }

    // Linear decay: penalty * max(0, 1 - accessCount / N).
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
