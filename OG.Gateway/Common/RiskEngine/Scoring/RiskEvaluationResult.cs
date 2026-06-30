using Application.Common.RiskEngine.Rules;
using Domain.Entities;

namespace Application.Common.RiskEngine.Scoring;

/// <summary>
/// Final outcome of a full risk evaluation for a single request (HU-015).
/// Carries the breakdown so the verdict is fully auditable (T-030).
/// </summary>
public sealed record RiskEvaluationResult(
    Guid EvaluationId,
    decimal PolicyScore,
    decimal AnomalyScore,
    decimal ColdStartPenalty,
    decimal RiskScore,
    Verdict Verdict,
    IReadOnlyList<RuleEvaluationResult> RuleResults);
