using Domain.Entities;

namespace Application.Common.RiskEngine.Rules;

// Open/Closed: new rule types are added by implementing this interface and registering it;
// the PolicyScoreCalculator selects the evaluator by Type without modification.
public interface IRuleEvaluator
{
    PolicyType Type { get; }

    // Returns the partial, unweighted contribution to the Policy Score.
    Task<RuleEvaluationResult> EvaluateAsync(
        RequestContext context,
        AccessPolicy policy,
        CancellationToken cancellationToken = default);
}
