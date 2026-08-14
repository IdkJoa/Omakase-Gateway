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
            .ExecuteRedisAsync(Arg.Any<Func<Task<string?>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<Task<string?>>>()());

        _circuitBreakerMock
            .ExecuteRedisAsync(Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<Task<bool>>>()());

        _circuitBreakerMock
            .ExecuteRedisAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<Task>>()());

        _resilientRedis = new ResilientRedisService(_innerMock, _circuitBreakerMock);
    }

    [Fact]
    public async Task GetCachedProfileAsync_ShouldExecuteThroughCircuitBreaker()
    {
        var expectedProfile = "{\"score\": 12}";
        _innerMock.GetCachedProfileAsync("user_1").Returns(expectedProfile);

        var result = await _resilientRedis.GetCachedProfileAsync("user_1");

        Assert.Equal(expectedProfile, result);
        await _circuitBreakerMock.Received(1).ExecuteRedisAsync(Arg.Any<Func<Task<string?>>>(), Arg.Any<CancellationToken>());
        await _innerMock.Received(1).GetCachedProfileAsync("user_1");
    }

    [Fact]
    public async Task IsBlacklistedAsync_ShouldExecuteThroughCircuitBreaker()
    {
        _innerMock.IsBlacklistedAsync("jti_revoked").Returns(true);

        var result = await _resilientRedis.IsBlacklistedAsync("jti_revoked");

        Assert.True(result);
        await _circuitBreakerMock.Received(1).ExecuteRedisAsync(Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>());
        await _innerMock.Received(1).IsBlacklistedAsync("jti_revoked");
    }
}
