using Application.Common.RiskEngine.Rules;

namespace Application.Common.RiskEngine.Scoring;

/// <summary>
/// Combines the partial results of the evaluated rules into a single
/// weighted Policy Score (0–100). T-028.
/// </summary>
public interface IPolicyScoreCalculator
{
    /// <summary>
    /// Weighted average of the rule scores using each policy's weight.
    /// Returns 0 when there are no rules or the total weight is zero.
    /// </summary>
    decimal Calculate(IReadOnlyList<RuleEvaluationResult> ruleResults);
}
