using Domain.Entities;

namespace Application.Common.RiskEngine.Rules;

/// <summary>
/// Contract for a deterministic policy rule (RF-M2): Geofence, Time-Window,
/// Fingerprint, Impossible-Travel.
/// <para>
/// Open/Closed: new rule types are added by implementing this interface and
/// registering a new <see cref="IRuleEvaluator"/>; the PolicyScoreCalculator
/// (HU-015) selects the evaluator by <see cref="Type"/> without modification.
/// </para>
/// </summary>
public interface IRuleEvaluator
{
    /// <summary>Policy type this evaluator handles; used for dispatch.</summary>
    PolicyType Type { get; }

    /// <summary>
    /// Evaluates the request against a single policy and returns its partial,
    /// unweighted contribution to the Policy Score.
    /// </summary>
    Task<RuleEvaluationResult> EvaluateAsync(
        RequestContext context,
        AccessPolicy policy,
        CancellationToken cancellationToken = default);
}
