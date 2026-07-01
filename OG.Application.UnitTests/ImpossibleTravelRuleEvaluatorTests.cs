using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Application.Common.RiskEngine;
using Application.Common.RiskEngine.Rules;
using Application.Common.RiskEngine.Rules.Helpers;
using Application.Common.Security;
using Domain.Common;
using Domain.Entities;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas unitarias para verificar el comportamiento de la regla Viaje Imposible (HU-014).
/// Cubre la precisión de la fórmula Haversine, casos de viaje posible y viaje imposible,
/// así como comportamiento de arranque en frío (cold start) y fallos de geolocalización.
/// </summary>
public class ImpossibleTravelRuleEvaluatorTests
{
    private readonly IGeoLocationService _geoMock;
    private readonly ILastAccessService _lastAccessMock;
    private readonly ILogger<ImpossibleTravelRuleEvaluator> _loggerMock;

    private static readonly AccessPolicy Policy = new()
    {
        Name = "impossible-travel-policy",
        Type = PolicyType.ImpossibleTravel,
        Weight = 0.8m,
        Config = JsonDocument.Parse("{}")
    };

    public ImpossibleTravelRuleEvaluatorTests()
    {
        _geoMock = Substitute.For<IGeoLocationService>();
        _lastAccessMock = Substitute.For<ILastAccessService>();
        _loggerMock = Substitute.For<ILogger<ImpossibleTravelRuleEvaluator>>();
    }

    private ImpossibleTravelRuleEvaluator CreateSut() => new(_geoMock, _lastAccessMock, _loggerMock);

    [Fact]
    public void Type_IsImpossibleTravel()
    {
        Assert.Equal(PolicyType.ImpossibleTravel, CreateSut().Type);
    }

    [Fact]
    public void Haversine_Distance_IsAccurate()
    {
        // Coordenadas conocidas:
        // Santo Domingo (RD): 18.4861, -69.9312
        // Miami (US): 25.7617, -80.1918
        // Distancia real aproximada: ~1330 km

        var dist = Haversine.Distance(18.4861, -69.9312, 25.7617, -80.1918);

        Assert.True(dist > 1300 && dist < 1350, $"La distancia calculada {dist:F1} km debe estar alrededor de ~1330 km.");
    }

    [Fact]
    public async Task EvaluateAsync_AnonymousUser_ReturnsCoherentScore()
    {
        // Arrange
        var context = new RequestContext
        {
            SourceIp = "127.0.0.1",
            UserId = null // Usuario anónimo
        };

        // Act
        var result = await CreateSut().EvaluateAsync(context, Policy);

        // Assert
        Assert.Equal(0m, result.Score);
        Assert.False(result.Triggered);
        Assert.Equal("anonymous_user", result.Detail);
    }

    [Fact]
    public async Task EvaluateAsync_CurrentGeoUnavailable_ReturnsCoherentScore_FailSafe()
    {
        // Arrange
        var context = new RequestContext
        {
            SourceIp = "8.8.8.8",
            UserId = "user-123"
        };

        // Simular que el servicio geo falla para la IP actual
        _geoMock.ResolveAsync("8.8.8.8", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<GeoResult>(GeoErrors.Unavailable));

        // Act
        var result = await CreateSut().EvaluateAsync(context, Policy);

        // Assert
        Assert.Equal(0m, result.Score); // Se omite la regla para evitar falsos positivos
        Assert.False(result.Triggered);
        Assert.Equal("current_geo_unavailable", result.Detail);
    }

    [Fact]
    public async Task EvaluateAsync_NoHistory_ReturnsCoherentScore_ColdStart()
    {
        // Arrange
        var context = new RequestContext
        {
            SourceIp = "8.8.8.8",
            UserId = "user-123"
        };

        // Geolocalización actual exitosa en Miami (US)
        _geoMock.ResolveAsync("8.8.8.8", Arg.Any<CancellationToken>())
            .Returns(Result.Success(new GeoResult("US", "Miami", 25.7617, -80.1918)));

        // No hay accesos previos registrados para el usuario
        _lastAccessMock.GetLastAccessAsync("user-123", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<LastAccessResult?>(null));

        // Act
        var result = await CreateSut().EvaluateAsync(context, Policy);

        // Assert
        Assert.Equal(0m, result.Score);
        Assert.False(result.Triggered);
        Assert.Equal("no_history", result.Detail);
    }

    [Fact]
    public async Task EvaluateAsync_TravelPlausible_ReturnsCoherentScore()
    {
        // Arrange
        var requestTime = DateTimeOffset.UtcNow;
        var context = new RequestContext
        {
            SourceIp = "8.8.8.8",
            UserId = "user-123",
            Timestamp = requestTime
        };

        // Petición actual llega desde Miami, US (25.7617, -80.1918)
        _geoMock.ResolveAsync("8.8.8.8", Arg.Any<CancellationToken>())
            .Returns(Result.Success(new GeoResult("US", "Miami", 25.7617, -80.1918)));

        // Último acceso hace 8 horas desde Santo Domingo, RD (18.4861, -69.9312)
        var lastAccess = new LastAccessResult(
            Timestamp: requestTime.AddHours(-8),
            Latitude: 18.4861,
            Longitude: -69.9312);

        _lastAccessMock.GetLastAccessAsync("user-123", Arg.Any<CancellationToken>())
            .Returns(lastAccess);

        // Act
        var result = await CreateSut().EvaluateAsync(context, Policy);

        // Assert
        Assert.Equal(0m, result.Score);
        Assert.False(result.Triggered);
        Assert.StartsWith("travel_plausible", result.Detail);
    }

    [Fact]
    public async Task EvaluateAsync_ImpossibleTravel_ReturnsSevereScore()
    {
        // Arrange
        var requestTime = DateTimeOffset.UtcNow;
        var context = new RequestContext
        {
            SourceIp = "8.8.8.8",
            UserId = "user-123",
            Timestamp = requestTime
        };

        // Petición actual llega desde Tokio, JP (35.6762, 139.6503)
        _geoMock.ResolveAsync("8.8.8.8", Arg.Any<CancellationToken>())
            .Returns(Result.Success(new GeoResult("JP", "Tokyo", 35.6762, 139.6503)));

        // Último acceso hace solo 20 minutos (0.33 horas) desde Santo Domingo, RD (18.4861, -69.9312)
        // Distancia aproximada de ~14000 km.
        // Velocidad implícita: 14000 / 0.33 = ~42000 km/h (>900 km/h)
        var lastAccess = new LastAccessResult(
            Timestamp: requestTime.AddMinutes(-20),
            Latitude: 18.4861,
            Longitude: -69.9312);

        _lastAccessMock.GetLastAccessAsync("user-123", Arg.Any<CancellationToken>())
            .Returns(lastAccess);

        // Act
        var result = await CreateSut().EvaluateAsync(context, Policy);

        // Assert
        Assert.Equal(100m, result.Score);
        Assert.True(result.Triggered);
        Assert.StartsWith("impossible_travel_detected", result.Detail);
    }
}
