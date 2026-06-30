using System.Text.Json;
using Application.Common.RiskEngine.Rules;
using Application.Common.Security;
using Domain.Entities;
using Domain.ValueObjects;

namespace Application.Common.RiskEngine.Scoring;

/// <summary>
/// Default risk-evaluation orchestrator (HU-015). Wires the per-service policy
/// loading, rule dispatch (by <see cref="IRuleEvaluator.Type"/>), Policy Score
/// weighting, Risk Score consolidation and the audit write.
/// <para>Open/Closed: new rules participate automatically by registering a new
/// <see cref="IRuleEvaluator"/>; this class never changes.</para>
/// </summary>
public sealed class RiskEvaluationService : IRiskEvaluationService
{
    private readonly IServicePolicyProvider _policyProvider;
    private readonly IEnumerable<IRuleEvaluator> _evaluators;
    private readonly IPolicyScoreCalculator _policyScore;
    private readonly IRiskScoreConsolidator _consolidator;
    private readonly IRiskConfigProvider _configProvider;
    private readonly IGeoLocationService _geo;
    private readonly IAuditWriter _audit;

    public RiskEvaluationService(
        IServicePolicyProvider policyProvider,
        IEnumerable<IRuleEvaluator> evaluators,
        IPolicyScoreCalculator policyScore,
        IRiskScoreConsolidator consolidator,
        IRiskConfigProvider configProvider,
        IGeoLocationService geo,
        IAuditWriter audit)
    {
        _policyProvider = policyProvider;
        _evaluators = evaluators;
        _policyScore = policyScore;
        _consolidator = consolidator;
        _configProvider = configProvider;
        _geo = geo;
        _audit = audit;
    }

    public async Task<RiskEvaluationResult> EvaluateAsync(
        RequestContext context,
        ProtectedServiceId serviceId,
        decimal anomalyScore,
        int accessCount,
        CancellationToken cancellationToken = default)
    {
        var config = await _configProvider.GetAsync(cancellationToken);
        var policies = await _policyProvider.GetActivePoliciesAsync(serviceId, cancellationToken);

        // Run only the rules whose evaluator is registered; unknown types are skipped.
        var ruleResults = new List<RuleEvaluationResult>();
        foreach (var policy in policies)
        {
            var evaluator = _evaluators.FirstOrDefault(e => e.Type == policy.Type);
            if (evaluator is null)
                continue;

            ruleResults.Add(await evaluator.EvaluateAsync(context, policy, cancellationToken));
        }

        var policyScore = _policyScore.Calculate(ruleResults);
        var consolidated = _consolidator.Consolidate(policyScore, anomalyScore, accessCount, config);

        var result = new RiskEvaluationResult(
            EvaluationId: Guid.CreateVersion7(),
            PolicyScore: policyScore,
            AnomalyScore: anomalyScore,
            ColdStartPenalty: consolidated.ColdStartPenalty,
            RiskScore: consolidated.RiskScore,
            Verdict: consolidated.Verdict,
            RuleResults: ruleResults);

        await _audit.WriteAsync(await BuildAuditLogAsync(context, serviceId, result, cancellationToken), cancellationToken);

        return result;
    }

    private async Task<AuditLog> BuildAuditLogAsync(
        RequestContext context,
        ProtectedServiceId serviceId,
        RiskEvaluationResult result,
        CancellationToken cancellationToken)
    {
        var geo = await _geo.ResolveAsync(context.SourceIp, cancellationToken);

        JsonDocument? geoJson = geo is null
            ? null
            : JsonSerializer.SerializeToDocument(new
            {
                country = geo.CountryCode,
                city = geo.City,
                lat = geo.Latitude,
                lon = geo.Longitude,
            });

        // Only the triggered rules with their partial scores (T-030).
        var triggered = result.RuleResults
            .Where(r => r.Triggered)
            .Select(r => new { rule = r.RuleName, score = r.Score, detail = r.Detail })
            .ToArray();

        var triggeredJson = JsonSerializer.SerializeToDocument(triggered);

        return new AuditLog
        {
            Id = AuditLogId.New(),
            EvaluationId = result.EvaluationId,
            UserId = TryParseUserId(context.UserId),
            ServiceId = serviceId,
            SourceIp = context.SourceIp,
            Geo = geoJson,
            UserAgent = context.UserAgent,
            FingerprintHash = null, // poblado por la regla Fingerprint (HU-013)
            PolicyScore = result.PolicyScore,
            AnomalyScore = result.AnomalyScore,
            RiskScore = result.RiskScore,
            Verdict = result.Verdict,
            TriggeredRules = triggeredJson,
            EvaluatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static UserId? TryParseUserId(string? userId)
        => Guid.TryParse(userId, out var guid) ? UserId.From(guid) : null;
}
