using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Application.Common.RiskEngine;
using Application.Common.RiskEngine.Rules;
using Application.Common.RiskEngine.Scoring;
using Application.Common.Security;
using Domain.Entities;
using Domain.ValueObjects;
using NSubstitute;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas del orquestador RiskEvaluationService (HU-015). Usa el calculador y
/// el consolidador reales, y mockea los puertos de datos. Cubre el escenario
/// "políticas por servicio" y el registro en auditoría (T-030).
/// </summary>
public class RiskEvaluationServiceTests
{
    private readonly IServicePolicyProvider _policyProvider = Substitute.For<IServicePolicyProvider>();
    private readonly IRiskConfigProvider _configProvider = Substitute.For<IRiskConfigProvider>();
    private readonly IGeoLocationService _geo = Substitute.For<IGeoLocationService>();
    private readonly IAuditWriter _audit = Substitute.For<IAuditWriter>();

    private static readonly RequestContext Context = new() { SourceIp = "190.166.12.45" };

    public RiskEvaluationServiceTests()
    {
        _configProvider.GetAsync(Arg.Any<CancellationToken>()).Returns(Config());
        _geo.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GeoResult?>(null));
        _audit.WriteAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
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

    private static AccessPolicy Policy(string name, decimal weight = 1m) => new()
    {
        Id = AccessPolicyId.New(),
        Name = name,
        Type = PolicyType.Geofence,
        Weight = weight,
        Config = JsonDocument.Parse("{}"),
        IsActive = true,
        CreatedById = UserId.New(),
    };

    /// <summary>Stub: score 100 para políticas "high", 0 para el resto.</summary>
    private sealed class StubGeofence : IRuleEvaluator
    {
        public PolicyType Type => PolicyType.Geofence;
        public Task<RuleEvaluationResult> EvaluateAsync(RequestContext c, AccessPolicy p, CancellationToken ct = default)
        {
            var score = p.Name == "high" ? 100m : 0m;
            return Task.FromResult(new RuleEvaluationResult("GEOFENCE", score, p.Weight, score > 0));
        }
    }

    private RiskEvaluationService CreateSut(IEnumerable<IRuleEvaluator> evaluators) => new(
        _policyProvider,
        evaluators,
        new PolicyScoreCalculator(),
        new RiskScoreConsolidator(),
        _configProvider,
        _geo,
        _audit);

    // Escenario backlog — Políticas por servicio: cada servicio evalúa solo las suyas.
    [Fact]
    public async Task PerService_DifferentPolicies_ProduceDifferentScores()
    {
        var serviceA = ProtectedServiceId.New(); // 2 políticas: 100 y 0 -> policyScore 50
        var serviceB = ProtectedServiceId.New(); // 1 política: 100  -> policyScore 100

        _policyProvider.GetActivePoliciesAsync(serviceA, Arg.Any<CancellationToken>())
            .Returns(new List<AccessPolicy> { Policy("high"), Policy("low") });
        _policyProvider.GetActivePoliciesAsync(serviceB, Arg.Any<CancellationToken>())
            .Returns(new List<AccessPolicy> { Policy("high") });

        var sut = CreateSut(new IRuleEvaluator[] { new StubGeofence() });

        var resultA = await sut.EvaluateAsync(Context, serviceA, anomalyScore: 0m, accessCount: 100);
        var resultB = await sut.EvaluateAsync(Context, serviceB, anomalyScore: 0m, accessCount: 100);

        Assert.Equal(50m, resultA.PolicyScore);
        Assert.Equal(100m, resultB.PolicyScore);
        Assert.NotEqual(resultA.PolicyScore, resultB.PolicyScore);
    }

    [Fact]
    public async Task Evaluate_WritesAuditOncePerRequest()
    {
        var serviceId = ProtectedServiceId.New();
        _policyProvider.GetActivePoliciesAsync(serviceId, Arg.Any<CancellationToken>())
            .Returns(new List<AccessPolicy> { Policy("high") });

        var sut = CreateSut(new IRuleEvaluator[] { new StubGeofence() });

        await sut.EvaluateAsync(Context, serviceId, anomalyScore: 0m, accessCount: 100);

        await _audit.Received(1).WriteAsync(Arg.Any<AuditLog>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PolicyWithoutRegisteredEvaluator_IsSkipped()
    {
        var serviceId = ProtectedServiceId.New();
        var timeWindow = Policy("tw");
        timeWindow.Type = PolicyType.TimeWindow; // no hay evaluador registrado para este tipo

        _policyProvider.GetActivePoliciesAsync(serviceId, Arg.Any<CancellationToken>())
            .Returns(new List<AccessPolicy> { timeWindow });

        var sut = CreateSut(new IRuleEvaluator[] { new StubGeofence() });

        var result = await sut.EvaluateAsync(Context, serviceId, anomalyScore: 0m, accessCount: 100);

        Assert.Empty(result.RuleResults);   // la política sin evaluador no se ejecuta
        Assert.Equal(0m, result.PolicyScore);
    }

    [Fact]
    public async Task HighPolicyScore_ProducesChallengeVerdict()
    {
        var serviceId = ProtectedServiceId.New();
        _policyProvider.GetActivePoliciesAsync(serviceId, Arg.Any<CancellationToken>())
            .Returns(new List<AccessPolicy> { Policy("high") });

        var sut = CreateSut(new IRuleEvaluator[] { new StubGeofence() });

        // policyScore 100 -> risk = 0.6*100 = 60 -> CHALLENGE
        var result = await sut.EvaluateAsync(Context, serviceId, anomalyScore: 0m, accessCount: 100);

        Assert.Equal(60m, result.RiskScore);
        Assert.Equal(Verdict.Challenge, result.Verdict);
    }
}
