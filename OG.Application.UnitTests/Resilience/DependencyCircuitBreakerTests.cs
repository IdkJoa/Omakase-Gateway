using System.Data.Common;
using System.Net.Sockets;
using Infrastructure.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NSubstitute;
using Polly.CircuitBreaker;
using StackExchange.Redis;
using Xunit;

namespace OG.Application.UnitTests.Resilience;

/// <summary>
/// Pruebas unitarias para verificar el comportamiento de DependencyCircuitBreaker (T-067 / HU-031).
/// Comprueba que 3 fallos consecutivos abren el circuito y responden de manera fail-closed.
/// </summary>
public class DependencyCircuitBreakerTests
{
    private readonly ILogger<DependencyCircuitBreaker> _loggerMock;
    private readonly DependencyCircuitBreaker _circuitBreaker;

    public DependencyCircuitBreakerTests()
    {
        _loggerMock = Substitute.For<ILogger<DependencyCircuitBreaker>>();
        _circuitBreaker = new DependencyCircuitBreaker(_loggerMock);
    }

    [Fact]
    public void InitialState_ShouldBeClosedForBothDependencies()
    {
        Assert.False(_circuitBreaker.IsRedisCircuitOpen);
        Assert.False(_circuitBreaker.IsPostgresCircuitOpen);
        Assert.Equal(CircuitState.Closed, _circuitBreaker.RedisCircuitState);
        Assert.Equal(CircuitState.Closed, _circuitBreaker.PostgresCircuitState);
    }

    [Fact]
    public async Task Redis_ThreeConsecutiveFailures_ShouldOpenCircuit()
    {
        // Arrange
        Func<Task<int>> failingRedisOperation = () => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis indisponible");

        // Act: 3 fallos consecutivos
        for (int i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<RedisConnectionException>(() => _circuitBreaker.ExecuteRedisAsync(failingRedisOperation));
        }

        // Assert: El circuito debe estar ABIERTO
        Assert.True(_circuitBreaker.IsRedisCircuitOpen);
        Assert.Equal(CircuitState.Open, _circuitBreaker.RedisCircuitState);

        // Cualquier intento subsiguiente mientras esté abierto debe lanzar BrokenCircuitException inmediatamente
        await Assert.ThrowsAsync<BrokenCircuitException>(() => _circuitBreaker.ExecuteRedisAsync(async () => 42));
    }

    [Fact]
    public async Task Postgres_ThreeConsecutiveFailures_ShouldOpenCircuit()
    {
        // Arrange
        Func<Task<string>> failingPostgresOperation = () => throw new NpgsqlException("PostgreSQL no responde");

        // Act: 3 fallos consecutivos
        for (int i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<NpgsqlException>(() => _circuitBreaker.ExecutePostgresAsync(failingPostgresOperation));
        }

        // Assert: El circuito debe estar ABIERTO
        Assert.True(_circuitBreaker.IsPostgresCircuitOpen);
        Assert.Equal(CircuitState.Open, _circuitBreaker.PostgresCircuitState);

        // Operaciones subsiguientes deben fallar inmediatamente con BrokenCircuitException
        await Assert.ThrowsAsync<BrokenCircuitException>(() => _circuitBreaker.ExecutePostgresAsync(async () => "ok"));
    }

    [Fact]
    public async Task Redis_SuccessfulOperation_KeepsCircuitClosed()
    {
        // Act
        var result = await _circuitBreaker.ExecuteRedisAsync(async () => "test_data");

        // Assert
        Assert.Equal("test_data", result);
        Assert.False(_circuitBreaker.IsRedisCircuitOpen);
    }
}
