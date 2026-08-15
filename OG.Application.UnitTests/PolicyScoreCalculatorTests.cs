using System.Collections.Generic;
using Application.Common.RiskEngine.Rules;
using Application.Common.RiskEngine.Scoring;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas del PolicyScoreCalculator (T-028, revisado): agregación por MÁXIMO ponderado
/// (<c>max(Score × Weight)</c>), no promedio. La violación más fuerte manda; añadir
/// políticas que no disparan NO diluye la detección (Zero Trust + integridad del modelo).
/// </summary>
public class PolicyScoreCalculatorTests
{
    private readonly PolicyScoreCalculator _sut = new();

    private static RuleEvaluationResult Rule(decimal score, decimal weight) =>
        new("RULE", score, weight, Triggered: score > 0);

    [Fact]
    public void EmptyList_ReturnsZero()
    {
        Assert.Equal(0m, _sut.Calculate(new List<RuleEvaluationResult>()));
    }

    [Fact]
    public void NullList_ReturnsZero()
    {
        Assert.Equal(0m, _sut.Calculate(null!));
    }

    [Fact]
    public void AllWeightsZero_ReturnsZero()
    {
        var rules = new List<RuleEvaluationResult> { Rule(100m, 0m), Rule(50m, 0m) };
        Assert.Equal(0m, _sut.Calculate(rules));
    }

    [Fact]
    public void SingleRule_FullWeight_ReturnsItsScore()
    {
        var rules = new List<RuleEvaluationResult> { Rule(80m, 1m) };
        Assert.Equal(80m, _sut.Calculate(rules));
    }

    [Fact]
    public void SingleRule_PartialWeight_CapsContribution()
    {
        // Una regla down-weighted no puede maximizar el score: 100 × 0.5 = 50.
        var rules = new List<RuleEvaluationResult> { Rule(100m, 0.5m) };
        Assert.Equal(50m, _sut.Calculate(rules));
    }

    [Fact]
    public void StrongestViolation_Dominates()
    {
        // max(100×1, 50×1, 0×1) = 100. La violación más grave manda.
        var rules = new List<RuleEvaluationResult> { Rule(100m, 1m), Rule(50m, 1m), Rule(0m, 1m) };
        Assert.Equal(100m, _sut.Calculate(rules));
    }

    [Fact]
    public void WeightedMax_PicksHighestWeightedContribution()
    {
        // max(100×0.75, 0×0.25) = 75.
        var rules = new List<RuleEvaluationResult> { Rule(100m, 0.75m), Rule(0m, 0.25m) };
        Assert.Equal(75m, _sut.Calculate(rules));
    }

    [Fact]
    public void SevereViolation_NotDilutedByCleanPolicies()
    {
        // REGRESIÓN del hallazgo: con el promedio, (50 + 0 + 0)/3 = 16.67 (dilución).
        // Con máximo, la violación de 50 se mantiene íntegra pese a las 2 políticas limpias.
        var rules = new List<RuleEvaluationResult> { Rule(50m, 1m), Rule(0m, 1m), Rule(0m, 1m) };
        Assert.Equal(50m, _sut.Calculate(rules));
    }
}
