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
/// Pruebas del ajuste de veredicto por step-up MFA en el motor de riesgo
/// (HU-046: degradación Challenge→Allow con step-up vigente T-105 y escalada
/// Challenge→Block para clientes no interactivos T-106).
/// </summary>
public class EvaluateRiskHandlerStepUpTests
{
    private const string UserGuid = "0198c9a2-0000-7000-8000-000000000001";

    private readonly IServicePolicyProvider _policyProvider = Substitute.For<IServicePolicyProvider>();
    private readonly IRiskConfigProvider _configProvider = Substitute.For<IRiskConfigProvider>();
    private readonly IGeoLocationService _geo = Substitute.For<IGeoLocationService>();
    private readonly IUserProfileStore _profileStore = Substitute.For<IUserProfileStore>();
    private readonly IProfileUpdateChannel _profileChannel = Substitute.For<IProfileUpdateChannel>();
    private readonly IStepUpStore _stepUpStore = Substitute.For<IStepUpStore>();
    private readonly IUserMfaInfoProvider _userMfaInfo = Substitute.For<IUserMfaInfoProvider>();
    private readonly FingerprintService _fingerprints = new();

    public EvaluateRiskHandlerStepUpTests()
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

        // Perfil maduro: sin penalización de cold-start (access_count = N).
        _profileStore.GetAsync(UserGuid, Arg.Any<CancellationToken>())
            .Returns(new UserAnomalyProfile(
                Array.Empty<AnomalyFeatureVector>(), Array.Empty<UserAccessSample>(), 10, false));

        // Servicio con una política que produce Policy Score 50:
        // Risk = 0.6*50 + 0.4*50 + 0 = 50 → CHALLENGE (40 < 50 ≤ 75).
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
    }

    private sealed class FixedScoreRule : IRuleEvaluator
    {
        public PolicyType Type => PolicyType.Geofence;
        public Task<RuleEvaluationResult> EvaluateAsync(RequestContext c, AccessPolicy p, CancellationToken ct = default)
            => Task.FromResult(new RuleEvaluationResult("GEOFENCE", 50m, p.Weight, true, "stub"));
    }

    private EvaluateRiskHandler CreateSut() => new(
        _policyProvider, new IRuleEvaluator[] { new FixedScoreRule() },
        new PolicyScoreCalculator(), new RiskScoreConsolidator(),
        _configProvider, new StubAnomalyDetector(), _geo, _profileStore, _profileChannel,
        _stepUpStore, _userMfaInfo, _fingerprints);

    private static RequestContext Context() => new()
    {
        SourceIp = "190.166.12.45",
        UserAgent = "Mozilla/5.0",
        AcceptLanguage = "es-DO",
        AcceptEncoding = "gzip",
        UserId = UserGuid,
        ServiceName = "svc",
    };

    [Fact]
    public async Task Challenge_WithValidStepUp_SameDevice_DegradesToAllow()
    {
        var hash = _fingerprints.GenerateHash("Mozilla/5.0", "es-DO", "gzip");
        _userMfaInfo.GetAsync(UserGuid, Arg.Any<CancellationToken>())
            .Returns(new UserMfaInfo(IsInteractive: true, MfaEnabled: true));
        _stepUpStore.GetAsync(UserGuid)
            .Returns(new StepUpData(hash, DateTimeOffset.UtcNow));

        var result = await CreateSut().HandleAsync(new EvaluateRiskCommand(Context()));

        // El Risk Score NO se reduce: solo el veredicto se degrada (SRS §3.6).
        Assert.Equal(50m, result.RiskScore);
        Assert.Equal(Verdict.Allow, result.Verdict);
        Assert.Contains(MfaRuleNames.Satisfied, result.TriggeredRules!.RootElement.GetRawText());
    }

    [Fact]
    public async Task Challenge_WithStepUpFromOtherDevice_RemainsChallenge()
    {
        _userMfaInfo.GetAsync(UserGuid, Arg.Any<CancellationToken>())
            .Returns(new UserMfaInfo(IsInteractive: true, MfaEnabled: true));
        _stepUpStore.GetAsync(UserGuid)
            .Returns(new StepUpData("hash-de-otro-dispositivo", DateTimeOffset.UtcNow));

        var result = await CreateSut().HandleAsync(new EvaluateRiskCommand(Context()));

        Assert.Equal(Verdict.Challenge, result.Verdict);
        Assert.DoesNotContain(MfaRuleNames.Satisfied, result.TriggeredRules!.RootElement.GetRawText());
    }

    [Fact]
    public async Task Challenge_WithoutStepUp_RemainsChallenge()
    {
        _userMfaInfo.GetAsync(UserGuid, Arg.Any<CancellationToken>())
            .Returns(new UserMfaInfo(IsInteractive: true, MfaEnabled: true));
        _stepUpStore.GetAsync(UserGuid).Returns((StepUpData?)null);

        var result = await CreateSut().HandleAsync(new EvaluateRiskCommand(Context()));

        Assert.Equal(Verdict.Challenge, result.Verdict);
    }

    [Fact]
    public async Task Challenge_NonInteractiveClient_EscalatesToBlock_FailClosed()
    {
        _userMfaInfo.GetAsync(UserGuid, Arg.Any<CancellationToken>())
            .Returns(new UserMfaInfo(IsInteractive: false, MfaEnabled: false));

        var result = await CreateSut().HandleAsync(new EvaluateRiskCommand(Context()));

        Assert.Equal(Verdict.Block, result.Verdict);

        // T-106: se registran MFA_FAILED y la escalada en triggered_rules.
        var rules = result.TriggeredRules!.RootElement.GetRawText();
        Assert.Contains(MfaRuleNames.NonInteractiveEscalated, rules);
        Assert.Contains(MfaRuleNames.Failed, rules);

        // Un cliente no interactivo nunca consulta la ventana de step-up.
        await _stepUpStore.DidNotReceive().GetAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task Challenge_AnonymousRequest_DoesNotTouchStepUpStores()
    {
        var anonymous = Context() with { UserId = null };

        var result = await CreateSut().HandleAsync(new EvaluateRiskCommand(anonymous));

        // Sin identidad no hay step-up posible: el veredicto queda como esté
        // (aquí sigue en zona de desafío: 0.6*50 + 0.4*50 + 0 = 50).
        Assert.Equal(Verdict.Challenge, result.Verdict);
        await _userMfaInfo.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _stepUpStore.DidNotReceive().GetAsync(Arg.Any<string>());
    }
}
