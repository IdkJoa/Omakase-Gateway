using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Application.Common.RiskEngine;
using Application.Common.RiskEngine.Commands;
using Application.Common.RiskEngine.Rules;
using Application.Common.RiskEngine.Scoring;
using Application.Common.Security;
using Domain.Common;
using Domain.Entities;
using Domain.ValueObjects;
using NSubstitute;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas del motor de riesgo cableado (HU-015: EvaluateRiskHandler).
/// Usa calculadora/consolidador reales y stub de anomalía; mockea providers y geoloc.
/// </summary>
public class EvaluateRiskHandlerTests
{
    private readonly IServicePolicyProvider _policyProvider = Substitute.For<IServicePolicyProvider>();
    private readonly IRiskConfigProvider _configProvider = Substitute.For<IRiskConfigProvider>();
    private readonly IGeoLocationService _geo = Substitute.For<IGeoLocationService>();

    public EvaluateRiskHandlerTests()
    {
        _configProvider.GetAsync(Arg.Any<CancellationToken>()).Returns(Config());
        _geo.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(new GeoResult("DO", "Santo Domingo", 18.4, -69.9))));
    }

    private static RiskScoreConfig Config() => new()
    {
        Id = RiskScoreConfigId.New(),
        PolicyWeight = 0.6m,
        AnomalyWeight = 0.4m,
        ColdStartPenalty = 30m,
        ColdStartN = 10,
        ChallengeThreshold = 40m,
        BlockThreshold = 75m,
    };

    private static AccessPolicy GeoPolicy(decimal weight = 1m) => new()
    {
        Id = AccessPolicyId.New(),
        Name = "geo",
        Type = PolicyType.Geofence,
        Weight = weight,
        Config = JsonDocument.Parse("{}"),
        IsActive = true,
        CreatedById = UserId.New(),
    };

    private sealed class StubGeofence : IRuleEvaluator
    {
        private readonly decimal _score;
        public StubGeofence(decimal score) => _score = score;
        public PolicyType Type => PolicyType.Geofence;
        public Task<RuleEvaluationResult> EvaluateAsync(RequestContext c, AccessPolicy p, CancellationToken ct = default)
            => Task.FromResult(new RuleEvaluationResult("GEOFENCE", _score, p.Weight, _score > 0m, "stub"));
    }

    private EvaluateRiskHandler CreateSut(IEnumerable<IRuleEvaluator> evaluators) => new(
        _policyProvider, evaluators, new PolicyScoreCalculator(), new RiskScoreConsolidator(),
        _configProvider, new StubAnomalyDetector(), _geo);

    private static EvaluateRiskCommand CommandFor(string? serviceName) =>
        new(new RequestContext { SourceIp = "190.166.12.45", ServiceName = serviceName });

    [Fact]
    public async Task NoService_OnlyAnomaly_Allow()
    {
        // Sin servicio -> PolicyScore 0 -> Risk = 0.6*0 + 0.4*50 + 0 = 20 -> ALLOW
        var result = await CreateSut(Array.Empty<IRuleEvaluator>()).HandleAsync(CommandFor(null));

        Assert.Equal(0m, result.PolicyScore);
        Assert.Equal(50m, result.AnomalyScore);
        Assert.Equal(20m, result.RiskScore);
        Assert.Equal(Verdict.Allow, result.Verdict);
    }

    [Fact]
    public async Task ServiceWithSevereRule_Blocks_AndSetsServiceId()
    {
        var serviceId = ProtectedServiceId.New();
        _policyProvider.GetByServiceNameAsync("nominas", Arg.Any<CancellationToken>())
            .Returns(new ServicePolicySet(serviceId, new List<AccessPolicy> { GeoPolicy() }));

        var result = await CreateSut(new IRuleEvaluator[] { new StubGeofence(100m) }).HandleAsync(CommandFor("nominas"));

        // PolicyScore 100 -> Risk = 0.6*100 + 0.4*50 = 80 -> BLOCK
        Assert.Equal(100m, result.PolicyScore);
        Assert.Equal(80m, result.RiskScore);
        Assert.Equal(Verdict.Block, result.Verdict);
        Assert.Equal(serviceId.Value, result.ServiceId);
    }

    [Fact]
    public async Task ColdStartPenalty_IsZero_InSprint2()
    {
        // Sin +30 de cold-start (access_count = N): Risk = 20, no 50.
        var result = await CreateSut(Array.Empty<IRuleEvaluator>()).HandleAsync(CommandFor(null));
        Assert.Equal(20m, result.RiskScore);
    }

    // T-030 / arreglo HU-14: el audit debe llevar geo y reglas disparadas.
    [Fact]
    public async Task PopulatesAuditData_GeoAndTriggeredRules()
    {
        var serviceId = ProtectedServiceId.New();
        _policyProvider.GetByServiceNameAsync("svc", Arg.Any<CancellationToken>())
            .Returns(new ServicePolicySet(serviceId, new List<AccessPolicy> { GeoPolicy() }));

        var result = await CreateSut(new IRuleEvaluator[] { new StubGeofence(100m) }).HandleAsync(CommandFor("svc"));

        Assert.NotNull(result.Geo);
        Assert.Equal("DO", result.Geo!.RootElement.GetProperty("country").GetString());
        Assert.NotNull(result.TriggeredRules);
        Assert.Equal(1, result.TriggeredRules!.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ServiceNotFound_NoPolicies()
    {
        _policyProvider.GetByServiceNameAsync("ghost", Arg.Any<CancellationToken>())
            .Returns((ServicePolicySet?)null);

        var result = await CreateSut(new IRuleEvaluator[] { new StubGeofence(100m) }).HandleAsync(CommandFor("ghost"));

        Assert.Equal(0m, result.PolicyScore);
        Assert.Null(result.ServiceId);
    }
}

/// <summary>Stub de anomalía: 50 en Sprint 2.</summary>
public class StubAnomalyDetectorTests
{
    [Fact]
    public async Task ReturnsFifty()
    {
        var score = await new StubAnomalyDetector().GetAnomalyScoreAsync(
            new RequestContext { SourceIp = "1.1.1.1" });

        Assert.Equal(50m, score);
    }
}
