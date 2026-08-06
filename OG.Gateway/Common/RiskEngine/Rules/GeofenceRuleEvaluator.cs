using System.Text.Json;
using Application.Common.Security;
using Domain.Entities;

namespace Application.Common.RiskEngine.Rules;

/// <summary>
/// Geofencing rule (HU-011 / T-022). Resolves the request's source country via
/// <see cref="IGeoLocationService"/> and compares it against the allow/deny
/// lists stored in the policy's JSONB <see cref="AccessPolicy.Config"/>.
/// <para>
/// Config shape (any combination):
/// <code>{ "allowed_countries": ["DO","US","ES"], "denied_countries": ["JP"] }</code>
/// </para>
/// </summary>
public sealed class GeofenceRuleEvaluator : IRuleEvaluator
{
    /// <summary>Coherent context: country allowed.</summary>
    private const decimal CoherentScore = 0m;

    /// <summary>Severe violation: country denied or outside the allow list.</summary>
    private const decimal SevereScore = 100m;

    /// <summary>
    /// Fail-safe bump when the location cannot be resolved (RF-M9): EvaluateRiskHandler
    /// applies +15 PolicyScore globally when geo is degraded. This evaluator returns 0m
    /// to avoid double-counting.
    /// </summary>
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

        // Ubicación desconocida → no se puede verificar; aporta un riesgo base pequeño.
        if (geoResult.IsFailure)
        {
            return new RuleEvaluationResult(
                RuleName, GeoUnavailableScore, policy.Weight, Triggered: false, Detail: "geo_unavailable");
        }

        var country = geoResult.Value.CountryCode.Trim().ToUpperInvariant();
        var allowed = ReadCountryList(policy.Config, "allowed_countries");
        var denied = ReadCountryList(policy.Config, "denied_countries");

        // El deny explícito gana sobre todo lo demás.
        if (denied.Contains(country))
        {
            return new RuleEvaluationResult(
                RuleName, SevereScore, policy.Weight, Triggered: true, Detail: $"denied:{country}");
        }

        // Hay lista de permitidos y el país no está en ella → severo.
        if (allowed.Count > 0 && !allowed.Contains(country))
        {
            return new RuleEvaluationResult(
                RuleName, SevereScore, policy.Weight, Triggered: true, Detail: $"not_allowed:{country}");
        }

        // Permitido (o ninguna restricción aplicó) → coherente.
        return new RuleEvaluationResult(
            RuleName, CoherentScore, policy.Weight, Triggered: false, Detail: $"allowed:{country}");
    }

    /// <summary>
    /// Reads a JSONB string array (e.g. allowed_countries) into an
    /// uppercase, case-insensitive set. Returns empty when absent or malformed.
    /// </summary>
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
