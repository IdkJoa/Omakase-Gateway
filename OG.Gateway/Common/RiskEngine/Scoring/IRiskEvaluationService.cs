using Domain.ValueObjects;

namespace Application.Common.RiskEngine.Scoring;

/// <summary>
/// Orchestrates a full risk evaluation for a request against a target service:
/// loads the service's policies, runs the matching rule evaluators, computes the
/// Policy Score, consolidates the Risk Score and verdict, and records the audit.
/// </summary>
public interface IRiskEvaluationService
{
    /// <param name="anomalyScore">AI Anomaly Score (0–100). Supplied by the caller; the ML layer arrives in Sprint 3.</param>
    /// <param name="accessCount">Historical access count of the user, for cold-start. Sprint 3 sources it from the profile.</param>
    Task<RiskEvaluationResult> EvaluateAsync(
        RequestContext context,
        ProtectedServiceId serviceId,
        decimal anomalyScore,
        int accessCount,
        CancellationToken cancellationToken = default);
}
