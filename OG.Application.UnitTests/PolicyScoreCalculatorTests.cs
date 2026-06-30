using System.Collections.Generic;
using Application.Common.RiskEngine.Rules;
using Application.Common.RiskEngine.Scoring;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas del PolicyScoreCalculator (T-028): promedio ponderado de las reglas.
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
    public void TotalWeightZero_ReturnsZero()
    {
        var rules = new List<RuleEvaluationResult> { Rule(100m, 0m), Rule(50m, 0m) };
        Assert.Equal(0m, _sut.Calculate(rules));
    }

    [Fact]
    public void SingleRule_ReturnsItsScore()
    {
        var rules = new List<RuleEvaluationResult> { Rule(80m, 0.3m) };
        Assert.Equal(80m, _sut.Calculate(rules));
    }

    [Fact]
    public void EqualWeights_ReturnsArithmeticMean()
    {
        var rules = new List<RuleEvaluationResult> { Rule(100m, 1m), Rule(0m, 1m) };
        Assert.Equal(50m, _sut.Calculate(rules));
    }

    [Fact]
    public void DifferentWeights_ReturnsWeightedAverage()
    {
        // (100*0.75 + 0*0.25) / (0.75+0.25) = 75
        var rules = new List<RuleEvaluationResult> { Rule(100m, 0.75m), Rule(0m, 0.25m) };
        Assert.Equal(75m, _sut.Calculate(rules));
    }
}
