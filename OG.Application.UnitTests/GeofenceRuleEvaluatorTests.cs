using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Application.Common.RiskEngine;
using Application.Common.RiskEngine.Rules;
using Application.Common.Security;
using Domain.Common;
using Domain.Entities;
using NSubstitute;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas unitarias de la regla Geofencing (HU-011 / T-022).
/// Cubre los escenarios del backlog (país permitido / país denegado),
/// la lista deny explícita, el fail-safe de geolocalización y el peso.
/// </summary>
public class GeofenceRuleEvaluatorTests
{
    private readonly IGeoLocationService _geo = Substitute.For<IGeoLocationService>();

    private static readonly RequestContext Context = new() { SourceIp = "190.166.12.45" };

    private static AccessPolicy PolicyWith(string configJson, decimal weight = 0.3m) => new()
    {
        Name = "geo-policy",
        Type = PolicyType.Geofence,
        Weight = weight,
        Config = JsonDocument.Parse(configJson),
    };

    private GeofenceRuleEvaluator CreateSut() => new(_geo);

    private void GeoReturns(string? countryCode)
    {
        _geo.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(countryCode is null
                ? Result.Failure<GeoResult>(GeoErrors.Unavailable)
                : Result.Success(new GeoResult(countryCode, "City", 0, 0))));
    }

    [Fact]
    public void Type_IsGeofence()
    {
        Assert.Equal(PolicyType.Geofence, CreateSut().Type);
    }

    // Escenario backlog — País permitido (lista [DO, US])
    [Theory]
    [InlineData("DO")]
    [InlineData("US")]
    public async Task AllowedCountry_GivesLowScore_NotTriggered(string country)
    {
        GeoReturns(country);
        var policy = PolicyWith("""{ "allowed_countries": ["DO","US"] }""");

        var result = await CreateSut().EvaluateAsync(Context, policy);

        Assert.Equal(0m, result.Score);
        Assert.False(result.Triggered);
        Assert.Equal("GEOFENCE", result.RuleName);
    }

    // Escenario backlog — País denegado (fuera de [DO, US])
    [Fact]
    public async Task CountryOutsideAllowList_GivesSevereScore_Triggered()
    {
        GeoReturns("JP");
        var policy = PolicyWith("""{ "allowed_countries": ["DO","US"] }""");

        var result = await CreateSut().EvaluateAsync(Context, policy);

        Assert.Equal(100m, result.Score);
        Assert.True(result.Triggered);
    }

    [Fact]
    public async Task DeniedCountry_GivesSevereScore_Triggered()
    {
        GeoReturns("JP");
        var policy = PolicyWith("""{ "denied_countries": ["JP"] }""");

        var result = await CreateSut().EvaluateAsync(Context, policy);

        Assert.Equal(100m, result.Score);
        Assert.True(result.Triggered);
    }

    [Fact]
    public async Task DenyWins_OverAllowList()
    {
        GeoReturns("US");
        var policy = PolicyWith("""{ "allowed_countries": ["US"], "denied_countries": ["US"] }""");

        var result = await CreateSut().EvaluateAsync(Context, policy);

        Assert.Equal(100m, result.Score);
        Assert.True(result.Triggered);
    }

    // Fail-safe (RF-M9): geolocalización no resuelve -> riesgo base, no 0
    [Fact]
    public async Task GeoUnavailable_GivesBaseScore_NotTriggered()
    {
        GeoReturns(null);
        var policy = PolicyWith("""{ "allowed_countries": ["DO"] }""");

        var result = await CreateSut().EvaluateAsync(Context, policy);

        Assert.Equal(15m, result.Score);
        Assert.False(result.Triggered);
        Assert.Equal("geo_unavailable", result.Detail);
    }

    [Fact]
    public async Task CarriesPolicyWeight_ForDownstreamWeighting()
    {
        GeoReturns("DO");
        var policy = PolicyWith("""{ "allowed_countries": ["DO"] }""", weight: 0.75m);

        var result = await CreateSut().EvaluateAsync(Context, policy);

        Assert.Equal(0.75m, result.Weight);
    }

    [Fact]
    public async Task CountryComparison_IsCaseInsensitive()
    {
        GeoReturns("do");
        var policy = PolicyWith("""{ "allowed_countries": ["DO"] }""");

        var result = await CreateSut().EvaluateAsync(Context, policy);

        Assert.Equal(0m, result.Score);
        Assert.False(result.Triggered);
    }
}
