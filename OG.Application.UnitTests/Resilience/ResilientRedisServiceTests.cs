using Application.Common.Security;
using Infrastructure.Resilience;
using NSubstitute;
using Xunit;

namespace OG.Application.UnitTests.Resilience;

/// <summary>
/// Pruebas unitarias para validar que ResilientRedisService enruta a través del Circuit Breaker (T-067 / HU-031).
/// </summary>
public class ResilientRedisServiceTests
{
    private readonly IRedisService _innerMock;
    private readonly IDependencyCircuitBreaker _circuitBreakerMock;
    private readonly ResilientRedisService _resilientRedis;

    public ResilientRedisServiceTests()
    {
        _innerMock = Substitute.For<IRedisService>();
        _circuitBreakerMock = Substitute.For<IDependencyCircuitBreaker>();

        // Configurar el mock del CircuitBreaker para ejecutar la función provista
        _circuitBreakerMock
            .ExecuteRedisAsync(Arg.Any<Func<Task<SessionData?>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<Task<SessionData?>>>()());

        _circuitBreakerMock
            .ExecuteRedisAsync(Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<Task<bool>>>()());

        _circuitBreakerMock
            .ExecuteRedisAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<Task>>()());

        _resilientRedis = new ResilientRedisService(_innerMock, _circuitBreakerMock);
    }

    [Fact]
    public async Task GetSessionAsync_ShouldExecuteThroughCircuitBreaker()
    {
        // Arrange
        var expectedSession = new SessionData("jti_123", "client", DateTimeOffset.UtcNow);
        _innerMock.GetSessionAsync("user_1").Returns(expectedSession);

        // Act
        var result = await _resilientRedis.GetSessionAsync("user_1");

        // Assert
        Assert.Equal(expectedSession, result);
        await _circuitBreakerMock.Received(1).ExecuteRedisAsync(Arg.Any<Func<Task<SessionData?>>>(), Arg.Any<CancellationToken>());
        await _innerMock.Received(1).GetSessionAsync("user_1");
    }

    [Fact]
    public async Task IsBlacklistedAsync_ShouldExecuteThroughCircuitBreaker()
    {
        // Arrange
        _innerMock.IsBlacklistedAsync("jti_revoked").Returns(true);

        // Act
        var result = await _resilientRedis.IsBlacklistedAsync("jti_revoked");

        // Assert
        Assert.True(result);
        await _circuitBreakerMock.Received(1).ExecuteRedisAsync(Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>());
        await _innerMock.Received(1).IsBlacklistedAsync("jti_revoked");
    }
}
