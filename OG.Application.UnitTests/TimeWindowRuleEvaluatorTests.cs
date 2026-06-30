using System;
using System.Text.Json;
using System.Threading.Tasks;
using Application.Common.RiskEngine;
using Application.Common.RiskEngine.Rules;
using Domain.Entities;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas unitarias de la regla Time-Window (HU-012 / T-023).
/// Valida la lógica de negocio para ventanas normales, ventanas que cruzan la medianoche (nocturnas),
/// y el comportamiento seguro Fail-Closed ante configuraciones malformadas.
/// </summary>
public class TimeWindowRuleEvaluatorTests
{
    private static AccessPolicy PolicyWith(string configJson, decimal weight = 0.5m) => new()
    {
        Name = "time-policy",
        Type = PolicyType.TimeWindow,
        Weight = weight,
        Config = JsonDocument.Parse(configJson),
    };

    private TimeWindowRuleEvaluator CreateSut() => new();

    [Fact]
    public void Type_IsTimeWindow()
    {
        Assert.Equal(PolicyType.TimeWindow, CreateSut().Type);
    }

    [Fact]
    public async Task InsideWindow_NormalRange_ReturnsCoherentScore()
    {
        // Arrange
        // Ventana: 08:00 a 18:00 AST (UTC-4)
        // Petición: 14:30 AST (Offset -4)
        var context = new RequestContext
        {
            SourceIp = "127.0.0.1",
            Timestamp = new DateTimeOffset(2026, 6, 30, 14, 30, 0, TimeSpan.FromHours(-4))
        };
        var policy = PolicyWith("""{ "start_time": "08:00", "end_time": "18:00", "timezone": "AST" }""");

        // Act
        var result = await CreateSut().EvaluateAsync(context, policy);

        // Assert
        Assert.Equal(0m, result.Score);
        Assert.False(result.Triggered);
        Assert.StartsWith("inside_window", result.Detail);
    }

    [Fact]
    public async Task OutsideWindow_NormalRange_ReturnsSevereScore()
    {
        // Arrange
        // Ventana: 08:00 a 18:00 AST
        // Petición: 02:00 AST
        var context = new RequestContext
        {
            SourceIp = "127.0.0.1",
            Timestamp = new DateTimeOffset(2026, 6, 30, 2, 0, 0, TimeSpan.FromHours(-4))
        };
        var policy = PolicyWith("""{ "start_time": "08:00", "end_time": "18:00", "timezone": "AST" }""");

        // Act
        var result = await CreateSut().EvaluateAsync(context, policy);

        // Assert
        Assert.Equal(100m, result.Score);
        Assert.True(result.Triggered);
        Assert.StartsWith("outside_window", result.Detail);
    }

    [Fact]
    public async Task InsideWindow_MidnightCrossing_ReturnsCoherentScore()
    {
        // Arrange
        // Ventana nocturna: 22:00 a 06:00 AST
        // Petición: 23:00 AST (Dentro)
        var context1 = new RequestContext
        {
            SourceIp = "127.0.0.1",
            Timestamp = new DateTimeOffset(2026, 6, 30, 23, 0, 0, TimeSpan.FromHours(-4))
        };
        // Petición: 04:00 AST (Dentro)
        var context2 = new RequestContext
        {
            SourceIp = "127.0.0.1",
            Timestamp = new DateTimeOffset(2026, 7, 1, 4, 0, 0, TimeSpan.FromHours(-4))
        };

        var policy = PolicyWith("""{ "start_time": "22:00", "end_time": "06:00", "timezone": "AST" }""");

        // Act & Assert
        var result1 = await CreateSut().EvaluateAsync(context1, policy);
        Assert.Equal(0m, result1.Score);
        Assert.False(result1.Triggered);

        var result2 = await CreateSut().EvaluateAsync(context2, policy);
        Assert.Equal(0m, result2.Score);
        Assert.False(result2.Triggered);
    }

    [Fact]
    public async Task OutsideWindow_MidnightCrossing_ReturnsSevereScore()
    {
        // Arrange
        // Ventana nocturna: 22:00 a 06:00 AST
        // Petición: 12:00 AST (Fuera)
        var context = new RequestContext
        {
            SourceIp = "127.0.0.1",
            Timestamp = new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.FromHours(-4))
        };
        var policy = PolicyWith("""{ "start_time": "22:00", "end_time": "06:00", "timezone": "AST" }""");

        // Act
        var result = await CreateSut().EvaluateAsync(context, policy);

        // Assert
        Assert.Equal(100m, result.Score);
        Assert.True(result.Triggered);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "start_time": "invalid", "end_time": "18:00", "timezone": "AST" }""")]
    [InlineData("""{ "start_time": "08:00", "end_time": "18:00", "timezone": "INVALID_TZ" }""")]
    public async Task MalformedConfig_ReturnsSevereScore_FailClosed(string badConfigJson)
    {
        // Arrange
        var context = new RequestContext
        {
            SourceIp = "127.0.0.1",
            Timestamp = DateTimeOffset.UtcNow
        };
        var policy = PolicyWith(badConfigJson);

        // Act
        var result = await CreateSut().EvaluateAsync(context, policy);

        // Assert
        Assert.Equal(100m, result.Score);
        Assert.True(result.Triggered);
        Assert.Equal("malformed_config", result.Detail);
    }

    [Fact]
    public async Task CarriesPolicyWeight()
    {
        // Arrange
        var context = new RequestContext
        {
            SourceIp = "127.0.0.1",
            Timestamp = new DateTimeOffset(2026, 6, 30, 14, 30, 0, TimeSpan.FromHours(-4))
        };
        var policy = PolicyWith("""{ "start_time": "08:00", "end_time": "18:00", "timezone": "AST" }""", weight: 0.85m);

        // Act
        var result = await CreateSut().EvaluateAsync(context, policy);

        // Assert
        Assert.Equal(0.85m, result.Weight);
    }
}
