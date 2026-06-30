using Application.Common.RiskEngine.Scoring;
using Domain.Entities;
using Domain.ValueObjects;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas del RiskScoreConsolidator (T-029): fórmula del Risk Score y veredicto.
/// Incluye los escenarios exactos del backlog (HU-015) y los bordes de umbral.
/// </summary>
public class RiskScoreConsolidatorTests
{
    private readonly RiskScoreConsolidator _sut = new();

    private static RiskScoreConfig Config() => new()
    {
        Id = RiskScoreConfigId.New(),
        PolicyWeight = 0.6m,
        AnomalyWeight = 0.4m,
        ColdStartPenalty = 30m,
        ColdStartN = 10,
        ChallengeThreshold = 40m, // <= 40 -> ALLOW
        BlockThreshold = 75m,     // <= 75 -> CHALLENGE, > 75 -> BLOCK
    };

    // Escenario backlog — Fórmula correcta: 0.6*30 + 0.4*50 + 0 = 38 -> ALLOW
    [Fact]
    public void Scenario_FormulaCorrecta_Returns38_Allow()
    {
        var result = _sut.Consolidate(policyScore: 30m, anomalyScore: 50m, accessCount: 10, Config());

        Assert.Equal(38m, result.RiskScore);
        Assert.Equal(0m, result.ColdStartPenalty);
        Assert.Equal(Verdict.Allow, result.Verdict);
    }

    // Escenario backlog — Cold start: cold=30*(1-2/10)=24; risk=12+8+24=44 -> CHALLENGE
    [Fact]
    public void Scenario_ColdStart_Returns44_Challenge()
    {
        var result = _sut.Consolidate(policyScore: 20m, anomalyScore: 20m, accessCount: 2, Config());

        Assert.Equal(24m, result.ColdStartPenalty);
        Assert.Equal(44m, result.RiskScore);
        Assert.Equal(Verdict.Challenge, result.Verdict);
    }

    [Fact]
    public void RiskExactlyAllowThreshold_IsAllow()
    {
        // 0.6*40 + 0.4*40 = 40 -> <= 40 -> ALLOW
        var result = _sut.Consolidate(40m, 40m, accessCount: 100, Config());

        Assert.Equal(40m, result.RiskScore);
        Assert.Equal(Verdict.Allow, result.Verdict);
    }

    [Fact]
    public void RiskExactlyBlockThreshold_IsChallenge()
    {
        // 0.6*75 + 0.4*75 = 75 -> <= 75 -> CHALLENGE
        var result = _sut.Consolidate(75m, 75m, accessCount: 100, Config());

        Assert.Equal(75m, result.RiskScore);
        Assert.Equal(Verdict.Challenge, result.Verdict);
    }

    [Fact]
    public void RiskAboveBlockThreshold_IsBlock()
    {
        var result = _sut.Consolidate(100m, 100m, accessCount: 100, Config());

        Assert.Equal(100m, result.RiskScore); // clamp a 100
        Assert.Equal(Verdict.Block, result.Verdict);
    }

    [Fact]
    public void NewUser_FullColdStartPenalty()
    {
        // accessCount=0 -> cold = 30 * (1 - 0/10) = 30
        var result = _sut.Consolidate(0m, 0m, accessCount: 0, Config());

        Assert.Equal(30m, result.ColdStartPenalty);
        Assert.Equal(30m, result.RiskScore);
    }

    [Fact]
    public void ColdStartN_Zero_NoDivisionByZero()
    {
        var config = Config();
        config.ColdStartN = 0;

        var result = _sut.Consolidate(10m, 10m, accessCount: 0, config);

        Assert.Equal(0m, result.ColdStartPenalty);
        Assert.Equal(10m, result.RiskScore);
    }
}
