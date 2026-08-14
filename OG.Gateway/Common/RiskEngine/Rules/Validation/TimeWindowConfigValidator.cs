using System.Text.Json;
using Domain.Entities;

namespace Application.Common.RiskEngine.Rules.Validation;

// Debe mantenerse en espejo con TimeWindowRuleEvaluator. El cruce de medianoche (start > end) es válido.
public sealed class TimeWindowConfigValidator : IPolicyConfigValidator
{
    // Abreviaturas con resolución propia en TimeWindowRuleEvaluator.GetTimeZone.
    private static readonly string[] SupportedAbbreviations = ["AST", "EST"];

    public PolicyType Type => PolicyType.TimeWindow;

    public IReadOnlyList<string> Validate(JsonDocument config)
    {
        var errors = new List<string>();
        var root = config.RootElement;

        ValidateTime(root, "start_time", errors);
        ValidateTime(root, "end_time", errors);

        if (!root.TryGetProperty("timezone", out var tzProp) ||
            tzProp.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(tzProp.GetString()))
        {
            errors.Add("La config de TimeWindow requiere 'timezone' (ej. \"America/Santo_Domingo\" o \"AST\").");
        }
        else if (!IsResolvableTimeZone(tzProp.GetString()!))
        {
            errors.Add($"La zona horaria '{tzProp.GetString()}' no es resoluble en el sistema.");
        }

        return errors;
    }

    private static void ValidateTime(JsonElement root, string property, List<string> errors)
    {
        if (!root.TryGetProperty(property, out var prop) ||
            prop.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(prop.GetString()))
        {
            errors.Add($"La config de TimeWindow requiere '{property}' como hora \"HH:mm\".");
            return;
        }

        if (!TimeSpan.TryParse(prop.GetString(), out _))
            errors.Add($"'{property}' no es una hora válida: se espera formato \"HH:mm\" (ej. \"08:00\").");
    }

    private static bool IsResolvableTimeZone(string tzName)
    {
        if (SupportedAbbreviations.Any(a => string.Equals(a, tzName, StringComparison.OrdinalIgnoreCase)))
            return true;

        return TimeZoneInfo.TryFindSystemTimeZoneById(tzName, out _);
    }
}
