using Domain.Entities;

namespace Application.Common.RiskEngine.Scoring;

public sealed record ConsolidatedRisk(decimal RiskScore, Verdict Verdict, decimal ColdStartPenalty);

// RiskScore = Wp*PolicyScore + Wa*AnomalyScore + ColdStartPenalty
public interface IRiskScoreConsolidator
{
    ConsolidatedRisk Consolidate(decimal policyScore, decimal anomalyScore, int accessCount, RiskScoreConfig config);
}
