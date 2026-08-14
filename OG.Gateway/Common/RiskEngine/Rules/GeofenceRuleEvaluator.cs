using System.Text.Json;
using Application.Common.Security;
using Domain.Entities;

namespace Application.Common.RiskEngine.Rules;

// Config shape (any combination): { "allowed_countries": ["DO","US","ES"], "denied_countries": ["JP"] }
public sealed class GeofenceRuleEvaluator : IRuleEvaluator
{
    private const decimal CoherentScore = 0m;

    private const decimal SevereScore = 100m;

    // 0m and not a penalty here: EvaluateRiskHandler already applies +15 PolicyScore globally
    // when geo is degraded, so scoring it again here would double-count.
    private const decimal GeoUnavailableScore = 0m;

    private const string RuleName = "GEOFENCE";

    private readonly IGeoLocationService _geo;

    public GeofenceRuleEvaluator(IGeoLocationService geo) => _geo = geo;

    public PolicyType Type => PolicyType.Geofence;

    public async Task<RuleEvaluationResult> EvaluateAsync(
        RequestContext context,
        AccessPolicy policy,
        CancellationToken cancellationToken = default)
    {
        var geoResult = await _geo.ResolveAsync(context.SourceIp, cancellationToken);

        if (geoResult.IsFailure)
        {
            return new RuleEvaluationResult(
                RuleName, GeoUnavailableScore, policy.Weight, Triggered: false, Detail: "geo_unavailable");
        }

        var country = geoResult.Value.CountryCode.Trim().ToUpperInvariant();
        var allowed = ReadCountryList(policy.Config, "allowed_countries");
        var denied = ReadCountryList(policy.Config, "denied_countries");

        // El deny explícito gana sobre el allow list.
        if (denied.Contains(country))
        {
            return new RuleEvaluationResult(
                RuleName, SevereScore, policy.Weight, Triggered: true, Detail: $"denied:{country}");
        }

        if (allowed.Count > 0 && !allowed.Contains(country))
        {
            return new RuleEvaluationResult(
                RuleName, SevereScore, policy.Weight, Triggered: true, Detail: $"not_allowed:{country}");
        }

        return new RuleEvaluationResult(
            RuleName, CoherentScore, policy.Weight, Triggered: false, Detail: $"allowed:{country}");
    }

    private static HashSet<string> ReadCountryList(JsonDocument config, string property)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (config is null)
            return set;

        if (!config.RootElement.TryGetProperty(property, out var element) ||
            element.ValueKind != JsonValueKind.Array)
        {
            return set;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var code = item.GetString();
                if (!string.IsNullOrWhiteSpace(code))
                    set.Add(code.Trim().ToUpperInvariant());
            }
        }

        return set;
    }
}
