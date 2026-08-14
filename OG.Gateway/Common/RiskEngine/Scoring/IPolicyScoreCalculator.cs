using Application.Common.RiskEngine.Rules;

namespace Application.Common.RiskEngine.Scoring;

public interface IPolicyScoreCalculator
{
    decimal Calculate(IReadOnlyList<RuleEvaluationResult> ruleResults);
}
