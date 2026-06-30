using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Application.Common.RiskEngine;
using Application.Common.RiskEngine.Rules;
using Application.Common.Security;
using Domain.Entities;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas unitarias para verificar el comportamiento de FingerprintRuleEvaluator (HU-013).
/// Valida el flujo en arranque en frío (primer registro), dispositivos conocidos,
/// dispositivos desconocidos (retorna 50), usuarios anónimos, y fallos de Redis (Fail-Closed parcial).
/// </summary>
public class FingerprintRuleEvaluatorTests
{
    private readonly IFingerprintService _fingerprintService;
    private readonly IRedisService _redisMock;
    private readonly ILogger<FingerprintRuleEvaluator> _loggerMock;

    private static readonly AccessPolicy Policy = new()
    {
        Name = "fingerprint-policy",
        Type = PolicyType.Fingerprint,
        Weight = 0.6m,
        Config = JsonDocument.Parse("{}")
    };

    public FingerprintRuleEvaluatorTests()
    {
        // Usamos la implementación real para el hashing del fingerprint ya que es puramente algorítmico y determinista.
        _fingerprintService = new FingerprintService();
        _redisMock = Substitute.For<IRedisService>();
        _loggerMock = Substitute.For<ILogger<FingerprintRuleEvaluator>>();
    }

    private FingerprintRuleEvaluator CreateSut() => new(_fingerprintService, _redisMock, _loggerMock);

    [Fact]
    public void Type_IsFingerprint()
    {
        Assert.Equal(PolicyType.Fingerprint, CreateSut().Type);
    }

    [Fact]
    public void FingerprintService_GenerateHash_IsDeterministicAndValid()
    {
        // Act
        var hash1 = _fingerprintService.GenerateHash("Mozilla", "es-ES", "gzip");
        var hash2 = _fingerprintService.GenerateHash("Mozilla", "es-ES", "gzip");
        var hashDifferent = _fingerprintService.GenerateHash("Mozilla", "en-US", "gzip");

        // Assert
        Assert.NotNull(hash1);
        Assert.Equal(64, hash1.Length); // Largo de SHA-256 en hexadecimal
        Assert.Equal(hash1, hash2);
        Assert.NotEqual(hash1, hashDifferent);
    }

    [Fact]
    public async Task EvaluateAsync_AnonymousUser_ReturnsCoherentScore()
    {
        // Arrange
        var context = new RequestContext
        {
            SourceIp = "127.0.0.1",
            UserId = null, // Usuario no autenticado / anónimo
            UserAgent = "Mozilla",
            AcceptLanguage = "es-ES",
            AcceptEncoding = "gzip"
        };

        // Act
        var result = await CreateSut().EvaluateAsync(context, Policy);

        // Assert
        Assert.Equal(0m, result.Score);
        Assert.False(result.Triggered);
        Assert.Equal("anonymous_user", result.Detail);
    }

    [Fact]
    public async Task EvaluateAsync_ColdStart_RegistersFirstDeviceAndReturnsCoherentScore()
    {
        // Arrange
        var context = new RequestContext
        {
            SourceIp = "127.0.0.1",
            UserId = "user-123",
            UserAgent = "Mozilla",
            AcceptLanguage = "es-ES",
            AcceptEncoding = "gzip"
        };

        var expectedHash = _fingerprintService.GenerateHash("Mozilla", "es-ES", "gzip");

        // Redis no tiene huellas registradas para este usuario
        _redisMock.GetFingerprintsAsync("user-123").Returns(new List<string>());

        // Act
        var result = await CreateSut().EvaluateAsync(context, Policy);

        // Assert
        Assert.Equal(0m, result.Score);
        Assert.False(result.Triggered);
        Assert.Equal("cold_start_registered", result.Detail);

        // Debe guardar la primera huella digital en Redis con TTL de 24h
        await _redisMock.Received(1).StoreFingerprintAsync("user-123", expectedHash, TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task EvaluateAsync_KnownDevice_RefreshesTtlAndReturnsCoherentScore()
    {
        // Arrange
        var context = new RequestContext
        {
            SourceIp = "127.0.0.1",
            UserId = "user-123",
            UserAgent = "Mozilla",
            AcceptLanguage = "es-ES",
            AcceptEncoding = "gzip"
        };

        var currentHash = _fingerprintService.GenerateHash("Mozilla", "es-ES", "gzip");

        // El usuario ya tiene este dispositivo registrado en Redis
        _redisMock.GetFingerprintsAsync("user-123").Returns(new List<string> { currentHash });

        // Act
        var result = await CreateSut().EvaluateAsync(context, Policy);

        // Assert
        Assert.Equal(0m, result.Score);
        Assert.False(result.Triggered);
        Assert.Equal("known_device", result.Detail);

        // Debe refrescar la expiración de la huella en Redis
        await _redisMock.Received(1).StoreFingerprintAsync("user-123", currentHash, TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task EvaluateAsync_UnknownDevice_RegistersNewDeviceAndReturnsPartialViolationScore()
    {
        // Arrange
        var context = new RequestContext
        {
            SourceIp = "127.0.0.1",
            UserId = "user-123",
            UserAgent = "Chrome-New-Device",
            AcceptLanguage = "es-ES",
            AcceptEncoding = "gzip"
        };

        var currentHash = _fingerprintService.GenerateHash("Chrome-New-Device", "es-ES", "gzip");
        var oldHash = _fingerprintService.GenerateHash("Mozilla", "es-ES", "gzip");

        // El usuario tiene registrado otro dispositivo pero no el actual
        _redisMock.GetFingerprintsAsync("user-123").Returns(new List<string> { oldHash });

        // Act
        var result = await CreateSut().EvaluateAsync(context, Policy);

        // Assert
        Assert.Equal(50m, result.Score);
        Assert.True(result.Triggered);
        Assert.Equal("unknown_device", result.Detail);

        // Debe guardar la nueva huella en Redis para asociarla a sus conocidos tras el reto
        await _redisMock.Received(1).StoreFingerprintAsync("user-123", currentHash, TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task EvaluateAsync_RedisConnectionFails_AppliesFailClosedPartialScore()
    {
        // Arrange
        var context = new RequestContext
        {
            SourceIp = "127.0.0.1",
            UserId = "user-123",
            UserAgent = "Mozilla",
            AcceptLanguage = "es-ES",
            AcceptEncoding = "gzip"
        };

        // Simular fallo de red/conexión con Redis
        _redisMock.GetFingerprintsAsync("user-123").Throws(new Exception("Redis timeout."));

        // Act
        var result = await CreateSut().EvaluateAsync(context, Policy);

        // Assert
        Assert.Equal(50m, result.Score); // Retorna score de riesgo parcial
        Assert.True(result.Triggered);
        Assert.Equal("redis_unavailable", result.Detail);
    }
}
