namespace Application.Common.RiskEngine.Rules;

// Score is the raw severity (0-100) of this rule alone; Weight is carried unapplied so that
// PolicyScoreCalculator does the weighting — the evaluator never pre-weights, to avoid double counting.
public sealed record RuleEvaluationResult(
    string RuleName,
    decimal Score,
    decimal Weight,
    bool Triggered,
    string? Detail = null);
