using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Domain.Entities;

namespace Application.Common.RiskEngine.Rules;

/// <summary>
/// Evaluador de ventanas horarias (HU-012 / T-023).
/// Valida si el momento en que se realiza la petición se encuentra dentro del rango
/// permitido configurado para el servicio en la zona horaria especificada.
/// <para>
/// Estructura de la configuración JSON de la política:
/// <code>
/// { "start_time": "08:00", "end_time": "18:00", "timezone": "AST" }
/// </code>
/// </para>
/// </summary>
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
            // Convertir la fecha y hora de la petición a la zona horaria destino
            var convertedTime = TimeZoneInfo.ConvertTime(context.Timestamp, tz);
            var timeOfDay = convertedTime.TimeOfDay;

            bool isInside;
            if (start <= end)
            {
                // Ventana normal (ej. 08:00 a 18:00)
                isInside = timeOfDay >= start && timeOfDay <= end;
            }
            else
            {
                // Cruce de medianoche (ej. 22:00 a 06:00 del día siguiente)
                isInside = timeOfDay >= start || timeOfDay <= end;
            }

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
            // Fail-Closed para cualquier fallo en el procesado o zona horaria inválida (Módulo 9)
            return Task.FromResult(new RuleEvaluationResult(
                RuleName, SevereScore, policy.Weight, Triggered: true, Detail: "malformed_config"));
        }
    }

    /// <summary>
    /// Resuelve de forma robusta y multiplataforma una zona horaria.
    /// Soporta abreviaciones comunes y detecta el sistema operativo (Windows/Linux).
    /// </summary>
    private static TimeZoneInfo GetTimeZone(string tzName)
    {
        if (string.Equals(tzName, "AST", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Atlantic Standard Time"); // Windows ID
            }
            catch
            {
                return TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo"); // IANA/Linux ID (UTC-4)
            }
        }
        if (string.Equals(tzName, "EST", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); // Windows ID
            }
            catch
            {
                return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); // IANA/Linux ID (UTC-5)
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
