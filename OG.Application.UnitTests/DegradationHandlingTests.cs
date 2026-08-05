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
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas unitarias para HU-032 (T-069 y T-070):
/// Degradación controlada ante fallo de GeoLocation y ML.NET.
/// </summary>
public class DegradationHandlingTests
{
    private readonly IServicePolicyProvider _policyProvider = Substitute.For<IServicePolicyProvider>();
    private readonly IRiskConfigProvider _configProvider = Substitute.For<IRiskConfigProvider>();
    private readonly IGeoLocationService _geo = Substitute.For<IGeoLocationService>();
    private readonly IAnomalyDetector _anomalyDetector = Substitute.For<IAnomalyDetector>();
    private readonly IUserProfileStore _profileStore = Substitute.For<IUserProfileStore>();
    private readonly IProfileUpdateChannel _profileChannel = Substitute.For<IProfileUpdateChannel>();
    private readonly IStepUpStore _stepUpStore = Substitute.For<IStepUpStore>();
    private readonly IUserMfaInfoProvider _userMfaInfo = Substitute.For<IUserMfaInfoProvider>();

    public DegradationHandlingTests()
    {
        _configProvider.GetAsync(Arg.Any<CancellationToken>()).Returns(Config());
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

    private EvaluateRiskHandler CreateSut(IEnumerable<IRuleEvaluator> evaluators) => new(
        _policyProvider, evaluators, new PolicyScoreCalculator(), new RiskScoreConsolidator(),
        _configProvider, _anomalyDetector, _geo, _profileStore, _profileChannel,
        _stepUpStore, _userMfaInfo, new FingerprintService());

    [Fact]
    public async Task GeoLocationFailure_ElevatesPolicyScoreBy15_AndRegistersServiceDegradation()
    {
        // Dado que GeoLocation falla / times out (Result.Failure)
        _geo.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<GeoResult>(GeoErrors.Unavailable)));

        _anomalyDetector.GetAnomalyScoreAsync(Arg.Any<RequestContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(50m));

        var command = new EvaluateRiskCommand(new RequestContext { SourceIp = "190.166.12.45" });
        var sut = CreateSut(Array.Empty<IRuleEvaluator>());

        // Cuando se evalúa la petición
        var result = await sut.HandleAsync(command);

        // Entonces el PolicyScore se eleva en +15 puntos (de 0 a 15)
        Assert.Equal(15m, result.PolicyScore);
        
        // Y en triggered_rules del audit se registra SERVICE_DEGRADATION
        Assert.NotNull(result.TriggeredRules);
        var jsonStr = result.TriggeredRules!.RootElement.GetRawText();
        Assert.Contains("SERVICE_DEGRADATION", jsonStr);
        Assert.Contains("geolocalizacion_inoperativa", jsonStr);
    }

    [Fact]
    public async Task MlNetException_FallbackToNeutralScore50_AndRegistersServiceDegradation()
    {
        // Dado que la inferencia de ML.NET falla y tira una excepción
        _geo.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(new GeoResult("DO", "Santo Domingo", 18.4, -69.9))));

        _anomalyDetector.GetAnomalyScoreAsync(Arg.Any<RequestContext>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Modelo ML.NET corrupto"));

        var command = new EvaluateRiskCommand(new RequestContext { SourceIp = "190.166.12.45" });
        var sut = CreateSut(Array.Empty<IRuleEvaluator>());

        // Cuando se evalúa la petición
        var result = await sut.HandleAsync(command);

        // Entonces el AnomalyScore decae a 50m neutro
        Assert.Equal(50m, result.AnomalyScore);

        // Y se registra la entrada SERVICE_DEGRADATION para ML.NET en audit
        Assert.NotNull(result.TriggeredRules);
        var jsonStr = result.TriggeredRules!.RootElement.GetRawText();
        Assert.Contains("SERVICE_DEGRADATION", jsonStr);
        Assert.Contains("ml_net_inoperativo", jsonStr);
    }
}
