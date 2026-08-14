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

// Fix HU-017: solo accesos CONCEDIDOS alimentan user_behavior_profiles. Si no, una cuenta nueva con
// credenciales robadas podría extinguir la penalización de cold-start a base de peticiones rechazadas
// (30 -> 27 -> 24 -> ...), entrar sin MFA (SRS §9.3, HU-046) y envenenar el baseline de RandomizedPCA (RF-M3).
public class EvaluateRiskHandlerColdStartIntegrityTests
{
    private const string UserGuid = "0198c9a2-0000-7000-8000-000000000002";

    private readonly IServicePolicyProvider _policyProvider = Substitute.For<IServicePolicyProvider>();
    private readonly IRiskConfigProvider _configProvider = Substitute.For<IRiskConfigProvider>();
    private readonly IGeoLocationService _geo = Substitute.For<IGeoLocationService>();
    private readonly IUserProfileStore _profileStore = Substitute.For<IUserProfileStore>();
    private readonly IProfileUpdateChannel _profileChannel = Substitute.For<IProfileUpdateChannel>();
    private readonly IStepUpStore _stepUpStore = Substitute.For<IStepUpStore>();
    private readonly IUserMfaInfoProvider _userMfaInfo = Substitute.For<IUserMfaInfoProvider>();
    private readonly FingerprintService _fingerprints = new();

    public EvaluateRiskHandlerColdStartIntegrityTests()
    {
        _configProvider.GetAsync(Arg.Any<CancellationToken>()).Returns(new RiskScoreConfig
        {
            Id = RiskScoreConfigId.New(),
            PolicyWeight = 0.6m,
            AnomalyWeight = 0.4m,
            ColdStartPenalty = 30m,
            ColdStartN = 10,
            ChallengeThreshold = 40m,
            BlockThreshold = 75m,
        });

        _geo.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(new GeoResult("DO", "Santo Domingo", 18.4, -69.9))));

        _userMfaInfo.GetAsync(UserGuid, Arg.Any<CancellationToken>())
            .Returns(new UserMfaInfo(IsInteractive: true, MfaEnabled: true));
    }

    /// <summary>Perfil nuevo (access_count = 0) ⇒ penalización máxima de cold-start.</summary>
    private void GivenBrandNewUser() =>
        _profileStore.GetAsync(UserGuid, Arg.Any<CancellationToken>())
            .Returns((UserAnomalyProfile?)null);

    private void GivenServiceWithRuleScore(decimal score) =>
        _policyProvider.GetByServiceNameAsync("svc", Arg.Any<CancellationToken>())
            .Returns(new ServicePolicySet(ProtectedServiceId.New(), new List<AccessPolicy>
            {
                new()
                {
                    Id = AccessPolicyId.New(),
                    Name = "geo",
                    Type = PolicyType.Geofence,
                    Weight = 1m,
                    Config = JsonDocument.Parse("{}"),
                    IsActive = true,
                    CreatedById = UserId.New(),
                }
            }));

    private sealed class FixedScoreRule(decimal score) : IRuleEvaluator
    {
        public PolicyType Type => PolicyType.Geofence;
        public Task<RuleEvaluationResult> EvaluateAsync(RequestContext c, AccessPolicy p, CancellationToken ct = default)
            => Task.FromResult(new RuleEvaluationResult("GEOFENCE", score, p.Weight, score > 0m, "stub"));
    }

    private EvaluateRiskHandler CreateSut(decimal ruleScore) => new(
        _policyProvider, new IRuleEvaluator[] { new FixedScoreRule(ruleScore) },
        new PolicyScoreCalculator(), new RiskScoreConsolidator(),
        _configProvider, new StubAnomalyDetector(), _geo, _profileStore, _profileChannel,
        _stepUpStore, _userMfaInfo, _fingerprints);

    private static EvaluateRiskCommand Command() => new(new RequestContext
    {
        SourceIp = "190.166.12.45",
        UserAgent = "Mozilla/5.0",
        AcceptLanguage = "es-DO",
        AcceptEncoding = "gzip",
        UserId = UserGuid,
        ServiceName = "svc",
    });

    [Fact]
    public async Task ChallengedRequest_DoesNotFeedProfile_SoColdStartCannotBeBurned()
    {
        GivenBrandNewUser();
        GivenServiceWithRuleScore(50m);

        // Risk = 0.6*50 + 0.4*50 + 30 = 80... el desafío queda en zona Challenge con regla 0:
        var result = await CreateSut(0m).HandleAsync(Command());

        Assert.Equal(Verdict.Challenge, result.Verdict);
        _profileChannel.DidNotReceive().TryWrite(Arg.Any<ProfileUpdate>());
    }

    [Fact]
    public async Task BlockedRequest_DoesNotFeedProfile_NorPoisonsMlBaseline()
    {
        GivenBrandNewUser();
        GivenServiceWithRuleScore(100m);

        // Risk = 0.6*100 + 0.4*50 + 30 = 110 -> clamp 100 -> BLOCK
        var result = await CreateSut(100m).HandleAsync(Command());

        Assert.Equal(Verdict.Block, result.Verdict);
        _profileChannel.DidNotReceive().TryWrite(Arg.Any<ProfileUpdate>());
    }

    [Fact]
    public async Task AllowedRequest_FeedsProfile()
    {
        // Perfil maduro (access_count = N) -> sin penalización: Risk = 0.4*50 = 20 -> ALLOW
        _profileStore.GetAsync(UserGuid, Arg.Any<CancellationToken>())
            .Returns(new UserAnomalyProfile(
                Array.Empty<AnomalyFeatureVector>(), Array.Empty<UserAccessSample>(), 10, false));
        GivenServiceWithRuleScore(0m);

        var result = await CreateSut(0m).HandleAsync(Command());

        Assert.Equal(Verdict.Allow, result.Verdict);
        _profileChannel.Received(1).TryWrite(Arg.Is<ProfileUpdate>(u => u.UserId == UserGuid));
    }

    [Fact]
    public async Task ChallengeDegradedByStepUp_FeedsProfile_LegitimateMfaAccessBuildsHistory()
    {
        GivenBrandNewUser();
        GivenServiceWithRuleScore(0m);
        _stepUpStore.GetAsync(UserGuid).Returns(new StepUpData(
            _fingerprints.GenerateHash("Mozilla/5.0", "es-DO", "gzip"), DateTimeOffset.UtcNow));

        var result = await CreateSut(0m).HandleAsync(Command());

        // El acceso se concedió tras un step-up válido: sí es comportamiento real del usuario.
        Assert.Equal(Verdict.Allow, result.Verdict);
        _profileChannel.Received(1).TryWrite(Arg.Any<ProfileUpdate>());
    }

    [Fact]
    public async Task NonInteractiveEscalatedToBlock_DoesNotFeedProfile()
    {
        GivenBrandNewUser();
        GivenServiceWithRuleScore(0m);
        _userMfaInfo.GetAsync(UserGuid, Arg.Any<CancellationToken>())
            .Returns(new UserMfaInfo(IsInteractive: false, MfaEnabled: false));

        var result = await CreateSut(0m).HandleAsync(Command());

        Assert.Equal(Verdict.Block, result.Verdict);
        _profileChannel.DidNotReceive().TryWrite(Arg.Any<ProfileUpdate>());
    }
}
