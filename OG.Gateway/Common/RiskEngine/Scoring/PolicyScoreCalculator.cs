using Application.Common.RiskEngine.Rules;

namespace Application.Common.RiskEngine.Scoring;

/// <summary>
/// Weighted-average Policy Score calculator (T-028).
/// Each rule contributes <c>Score × Weight</c>; the result is normalised by the
/// total weight so the Policy Score stays on the 0–100 scale regardless of how
/// many policies a service has associated.
/// </summary>
public sealed class PolicyScoreCalculator : IPolicyScoreCalculator
{
    public decimal Calculate(IReadOnlyList<RuleEvaluationResult> ruleResults)
    {
        if (ruleResults is null || ruleResults.Count == 0)
            return 0m;

        decimal weightedSum = 0m;
        decimal weightTotal = 0m;

        foreach (var rule in ruleResults)
        {
            weightedSum += rule.Score * rule.Weight;
            weightTotal += rule.Weight;
        }

        if (weightTotal <= 0m)
            return 0m;

        return weightedSum / weightTotal;
    }
}
