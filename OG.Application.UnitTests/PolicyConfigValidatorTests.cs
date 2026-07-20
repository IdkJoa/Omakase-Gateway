using System;
using System.Text.Json;
using Application.Common.RiskEngine.Rules.Validation;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Tests de HU-023 T-047 — validadores de config JSONB por tipo de regla.
/// Deben mantenerse en espejo con lo que parsean GeofenceRuleEvaluator y
/// TimeWindowRuleEvaluator: lo que estos tests aceptan, el motor lo evalúa;
/// lo que rechazan, el motor lo trataría como config malformada (fail-closed).
/// </summary>
public class PolicyConfigValidatorTests
{
    private static JsonDocument Json(string raw) => JsonDocument.Parse(raw);

    // ── Geofence ──────────────────────────────────────────────────────────────

    private readonly GeofenceConfigValidator _geofence = new();

    [Theory]
    [InlineData("""{ "allowed_countries": ["DO","US"] }""")]
    [InlineData("""{ "denied_countries": ["JP"] }""")]
    [InlineData("""{ "allowed_countries": ["do"], "denied_countries": ["JP"] }""")]
    public void Geofence_ConfigValida_SinErrores(string raw)
    {
        Assert.Empty(_geofence.Validate(Json(raw)));
    }

    [Theory]
    [InlineData("""{}""")]                                       // sin ninguna lista
    [InlineData("""{ "allowedCountries": ["DO"] }""")]           // camelCase: el motor no la lee
    [InlineData("""{ "allowed_countries": [] }""")]              // lista vacía: no restringe nada
    [InlineData("""{ "allowed_countries": "DO" }""")]            // no es array
    [InlineData("""{ "allowed_countries": ["DOM"] }""")]         // no es alpha-2
    [InlineData("""{ "allowed_countries": [12] }""")]            // no es string
    public void Geofence_ConfigInvalida_ConErrores(string raw)
    {
        Assert.NotEmpty(_geofence.Validate(Json(raw)));
    }

    // ── TimeWindow ────────────────────────────────────────────────────────────

    private readonly TimeWindowConfigValidator _timeWindow = new();

    [Theory]
    [InlineData("""{ "start_time": "08:00", "end_time": "20:00", "timezone": "America/Santo_Domingo" }""")]
    [InlineData("""{ "start_time": "22:00", "end_time": "06:00", "timezone": "AST" }""")]  // cruza medianoche
    [InlineData("""{ "start_time": "08:00", "end_time": "17:00", "timezone": "est" }""")]  // abreviatura, case-insensitive
    public void TimeWindow_ConfigValida_SinErrores(string raw)
    {
        Assert.Empty(_timeWindow.Validate(Json(raw)));
    }

    [Theory]
    [InlineData("""{}""")]                                                                   // faltan las tres claves
    [InlineData("""{ "start_time": "08:00", "end_time": "20:00" }""")]                       // falta timezone
    [InlineData("""{ "start_time": "25:99", "end_time": "20:00", "timezone": "AST" }""")]    // hora inválida
    [InlineData("""{ "start_time": "08:00", "end_time": "20:00", "timezone": "Marte/Olympus" }""")] // tz no resoluble
    [InlineData("""{ "startHour": 8, "endHour": 20, "daysOfWeek": [1] }""")]                 // claves del mock viejo
    public void TimeWindow_ConfigInvalida_ConErrores(string raw)
    {
        Assert.NotEmpty(_timeWindow.Validate(Json(raw)));
    }

    // ── Cobertura de tipos ────────────────────────────────────────────────────

    [Fact]
    public void CadaValidador_DeclaraSuTipo()
    {
        Assert.Equal(Domain.Entities.PolicyType.Geofence, _geofence.Type);
        Assert.Equal(Domain.Entities.PolicyType.TimeWindow, _timeWindow.Type);
    }
}
