namespace Application.Common.RiskEngine.Rules;

/// <summary>
/// Outcome produced by a single deterministic rule evaluator (RF-M2).
/// <para>
/// <see cref="Score"/> is the raw severity (0–100) of this rule alone.
/// <see cref="Weight"/> is carried from the policy so that the
/// PolicyScoreCalculator (HU-015) performs the weighting — the evaluator
/// never pre-weights, to avoid double counting.
/// </para>
/// </summary>
public sealed record RuleEvaluationResult(
    string RuleName,
    decimal Score,
    decimal Weight,
    bool Triggered,
    string? Detail = null);
