using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Domain.Entities;

namespace Application.Common.RiskEngine.Rules;

// Config shape: { "start_time": "08:00", "end_time": "18:00", "timezone": "AST" }
public sealed class TimeWindowRuleEvaluator : IRuleEvaluator
{
    private const decimal CoherentScore = 0m;
    private const decimal SevereScore = 100m;
    private const string RuleName = "TIME_WINDOW";

    public PolicyType Type => PolicyType.TimeWindow;

    public Task<RuleEvaluationResult> EvaluateAsync(
        RequestContext context,
        AccessPolicy policy,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (policy.Config is null)
            {
                return Task.FromResult(new RuleEvaluationResult(
                    RuleName, SevereScore, policy.Weight, Triggered: true, Detail: "malformed_config"));
            }

            var root = policy.Config.RootElement;
            if (!root.TryGetProperty("start_time", out var startProp) ||
                !root.TryGetProperty("end_time", out var endProp) ||
                !root.TryGetProperty("timezone", out var tzProp) ||
                startProp.ValueKind != JsonValueKind.String ||
                endProp.ValueKind != JsonValueKind.String ||
                tzProp.ValueKind != JsonValueKind.String)
            {
                return Task.FromResult(new RuleEvaluationResult(
                    RuleName, SevereScore, policy.Weight, Triggered: true, Detail: "malformed_config"));
            }

            var startTimeStr = startProp.GetString();
            var endTimeStr = endProp.GetString();
            var timezoneStr = tzProp.GetString();

            if (string.IsNullOrWhiteSpace(startTimeStr) ||
                string.IsNullOrWhiteSpace(endTimeStr) ||
                string.IsNullOrWhiteSpace(timezoneStr))
            {
                return Task.FromResult(new RuleEvaluationResult(
                    RuleName, SevereScore, policy.Weight, Triggered: true, Detail: "malformed_config"));
            }

            if (!TimeSpan.TryParse(startTimeStr, out var start) ||
                !TimeSpan.TryParse(endTimeStr, out var end))
            {
                return Task.FromResult(new RuleEvaluationResult(
                    RuleName, SevereScore, policy.Weight, Triggered: true, Detail: "malformed_config"));
            }

            var tz = GetTimeZone(timezoneStr);
            var convertedTime = TimeZoneInfo.ConvertTime(context.Timestamp, tz);
            var timeOfDay = convertedTime.TimeOfDay;

            // start > end significa que la ventana cruza medianoche (ej. 22:00 a 06:00).
            bool isInside = start <= end
                ? timeOfDay >= start && timeOfDay <= end
                : timeOfDay >= start || timeOfDay <= end;

            if (isInside)
            {
                return Task.FromResult(new RuleEvaluationResult(
                    RuleName, CoherentScore, policy.Weight, Triggered: false, Detail: $"inside_window:{convertedTime:HH:mm}"));
            }
            else
            {
                return Task.FromResult(new RuleEvaluationResult(
                    RuleName, SevereScore, policy.Weight, Triggered: true, Detail: $"outside_window:{convertedTime:HH:mm}"));
            }
        }
        catch (Exception)
        {
            // Fail-closed ante fallo de procesado o zona horaria inválida.
            return Task.FromResult(new RuleEvaluationResult(
                RuleName, SevereScore, policy.Weight, Triggered: true, Detail: "malformed_config"));
        }
    }

    // Windows y Linux usan IDs de zona horaria distintos (Windows ID vs IANA); se intenta el
    // Windows ID primero y se cae al IANA equivalente si FindSystemTimeZoneById falla.
    private static TimeZoneInfo GetTimeZone(string tzName)
    {
        if (string.Equals(tzName, "AST", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Atlantic Standard Time");
            }
            catch
            {
                return TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");
            }
        }
        if (string.Equals(tzName, "EST", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
            catch
            {
                return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            }
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(tzName);
        }
        catch
        {
            throw new TimeZoneNotFoundException($"La zona horaria '{tzName}' no es válida.");
        }
    }
}
