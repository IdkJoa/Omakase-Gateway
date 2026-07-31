using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Application.Common.RiskEngine;
using Application.Common.RiskEngine.AnomalyDetection;
using Application.Common.RiskEngine.Commands;
using Application.Common.RiskEngine.Rules;
using Application.Common.RiskEngine.Scoring;
using Application.Common.Security;
using Application.Common.Security.Mfa;
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

    private readonly IUserProfileStore _profileStore = Substitute.For<IUserProfileStore>();
    private readonly IProfileUpdateChannel _profileChannel = Substitute.For<IProfileUpdateChannel>();

    // HU-046: dependencias del step-up MFA. Por defecto: sin step-up vigente y sin info MFA.
    private readonly IStepUpStore _stepUpStore = Substitute.For<IStepUpStore>();
    private readonly IUserMfaInfoProvider _userMfaInfo = Substitute.For<IUserMfaInfoProvider>();

    private EvaluateRiskHandler CreateSut(IEnumerable<IRuleEvaluator> evaluators) => new(
        _policyProvider, evaluators, new PolicyScoreCalculator(), new RiskScoreConsolidator(),
        _configProvider, new StubAnomalyDetector(), _geo, _profileStore, _profileChannel,
        _stepUpStore, _userMfaInfo, new FingerprintService());

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

    // ── HU-017 / T-034: cold-start real desde el access_count del perfil ────────

    private void GivenProfileAccessCount(string userId, int accessCount) =>
        _profileStore.GetAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UserAnomalyProfile?>(new UserAnomalyProfile(
                Array.Empty<AnomalyFeatureVector>(), Array.Empty<UserAccessSample>(), accessCount, accessCount < 10)));

    private static EvaluateRiskCommand CommandForUser(string userId) =>
        new(new RequestContext { SourceIp = "190.166.12.45", UserId = userId });

    [Fact]
    public async Task NewUser_AccessCountZero_AppliesFullColdStart_Challenge()
    {
        GivenProfileAccessCount("u1", 0);

        // Risk = 0.6*0 + 0.4*50 + 30*(1 - 0/10) = 20 + 30 = 50 -> CHALLENGE
        var result = await CreateSut(Array.Empty<IRuleEvaluator>()).HandleAsync(CommandForUser("u1"));

        Assert.Equal(50m, result.RiskScore);
        Assert.Equal(Verdict.Challenge, result.Verdict);
    }

    [Fact]
    public async Task EstablishedUser_AccessCountReachesN_NoColdStart_Allow()
    {
        GivenProfileAccessCount("u1", 10);

        // Risk = 20 + 30*(1 - 10/10) = 20 -> ALLOW
        var result = await CreateSut(Array.Empty<IRuleEvaluator>()).HandleAsync(CommandForUser("u1"));

        Assert.Equal(20m, result.RiskScore);
        Assert.Equal(Verdict.Allow, result.Verdict);
    }

    [Fact]
    public async Task ColdStart_DecaysLinearly_AtHalfway()
    {
        GivenProfileAccessCount("u1", 5);

        // Risk = 20 + 30*(1 - 5/10) = 20 + 15 = 35
        var result = await CreateSut(Array.Empty<IRuleEvaluator>()).HandleAsync(CommandForUser("u1"));

        Assert.Equal(35m, result.RiskScore);
    }

    // ── HU-024 / SRS §7.5: requires_auth (exige JWT antes de evaluar) ────────────

    [Fact]
    public async Task ServiceRequiresAuth_NoIdentity_ShortCircuits_BeforeRulesAndMl()
    {
        var serviceId = ProtectedServiceId.New();
        _policyProvider.GetByServiceNameAsync("nominas", Arg.Any<CancellationToken>())
            .Returns(new ServicePolicySet(serviceId, new List<AccessPolicy> { GeoPolicy() }, RequiresAuth: true));

        // Petición ANÓNIMA (sin UserId) a un servicio con requires_auth. Aunque haya una regla severa,
        // debe cortarse ANTES de evaluar: AuthenticationRequired, sin correr reglas ni ML.
        var result = await CreateSut(new IRuleEvaluator[] { new StubGeofence(100m) }).HandleAsync(CommandFor("nominas"));

        Assert.True(result.AuthenticationRequired);
        Assert.Equal(serviceId.Value, result.ServiceId);
        Assert.Equal(0m, result.PolicyScore);   // reglas NO corridas
        Assert.Equal(0m, result.AnomalyScore);  // ML NO corrido
    }

    [Fact]
    public async Task ServiceRequiresAuth_WithIdentity_EvaluatesNormally()
    {
        var serviceId = ProtectedServiceId.New();
        _policyProvider.GetByServiceNameAsync("nominas", Arg.Any<CancellationToken>())
            .Returns(new ServicePolicySet(serviceId, new List<AccessPolicy> { GeoPolicy() }, RequiresAuth: true));
        GivenProfileAccessCount("u1", 10); // usuario establecido, sin cold-start

        var cmd = new EvaluateRiskCommand(new RequestContext
        {
            SourceIp = "190.166.12.45", ServiceName = "nominas", UserId = "u1"
        });
        var result = await CreateSut(new IRuleEvaluator[] { new StubGeofence(0m) }).HandleAsync(cmd);

        Assert.False(result.AuthenticationRequired); // con identidad no corta
        Assert.Equal(Verdict.Allow, result.Verdict);
    }

    [Fact]
    public async Task ServiceWithoutRequiresAuth_NoIdentity_EvaluatesNormally()
    {
        var serviceId = ProtectedServiceId.New();
        _policyProvider.GetByServiceNameAsync("publico", Arg.Any<CancellationToken>())
            .Returns(new ServicePolicySet(serviceId, new List<AccessPolicy>(), RequiresAuth: false));

        var result = await CreateSut(Array.Empty<IRuleEvaluator>()).HandleAsync(CommandFor("publico"));

        Assert.False(result.AuthenticationRequired); // requires_auth=false → sin gate
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
