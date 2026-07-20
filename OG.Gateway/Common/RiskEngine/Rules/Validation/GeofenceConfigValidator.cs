using System.Text.Json;
using Domain.Entities;

namespace Application.Common.RiskEngine.Rules.Validation;

/// <summary>
/// Valida la config de <see cref="PolicyType.Geofence"/> en espejo con
/// <c>GeofenceRuleEvaluator</c>: al menos una de <c>allowed_countries</c> /
/// <c>denied_countries</c> como array no vacío de códigos ISO 3166-1 alpha-2.
/// </summary>
public sealed class GeofenceConfigValidator : IPolicyConfigValidator
{
    public PolicyType Type => PolicyType.Geofence;

    public IReadOnlyList<string> Validate(JsonDocument config)
    {
        var errors = new List<string>();

        var hasAllowed = TryValidateCountryList(config, "allowed_countries", errors, out var allowedCount);
        var hasDenied = TryValidateCountryList(config, "denied_countries", errors, out var deniedCount);

        if (!hasAllowed && !hasDenied)
        {
            errors.Add("La config de Geofence requiere al menos una de las claves " +
                       "'allowed_countries' o 'denied_countries'.");
        }
        else if (allowedCount == 0 && deniedCount == 0)
        {
            errors.Add("Las listas de países de Geofence no pueden estar vacías: " +
                       "una política sin países nunca restringe nada.");
        }

        return errors;
    }

    /// <summary>Devuelve true si la clave existe; acumula errores de forma.</summary>
    private static bool TryValidateCountryList(
        JsonDocument config, string property, List<string> errors, out int count)
    {
        count = 0;

        if (!config.RootElement.TryGetProperty(property, out var element))
            return false;

        if (element.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"'{property}' debe ser un array de códigos de país (ej. [\"DO\",\"US\"]).");
            return true;
        }

        foreach (var item in element.EnumerateArray())
        {
            var code = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (code is null || code.Trim().Length != 2 || !code.Trim().All(char.IsLetter))
            {
                errors.Add($"'{property}' contiene un valor inválido: se esperan códigos " +
                           $"ISO 3166-1 alpha-2 de dos letras (ej. \"DO\").");
                return true;
            }
            count++;
        }

        return true;
    }
}
