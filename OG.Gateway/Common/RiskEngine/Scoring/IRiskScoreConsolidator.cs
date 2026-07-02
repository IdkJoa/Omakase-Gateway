using Domain.Entities;

namespace Application.Common.RiskEngine.Scoring;

/// <summary>Consolidated risk output: final score, verdict and the applied cold-start penalty.</summary>
public sealed record ConsolidatedRisk(decimal RiskScore, Verdict Verdict, decimal ColdStartPenalty);

/// <summary>
/// Applies the consolidated Risk Score formula and derives the verdict (T-029):
/// <code>RiskScore = Wp*PolicyScore + Wa*AnomalyScore + ColdStartPenalty</code>
/// </summary>
public interface IRiskScoreConsolidator
{
    ConsolidatedRisk Consolidate(decimal policyScore, decimal anomalyScore, int accessCount, RiskScoreConfig config);
}
