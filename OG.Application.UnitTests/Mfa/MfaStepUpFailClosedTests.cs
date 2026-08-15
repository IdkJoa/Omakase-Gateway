using System.Net;
using Application.Common.Security.Mfa;
using Infrastructure.Redis;
using Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Polly.CircuitBreaker;
using StackExchange.Redis;
using Xunit;

namespace OG.Application.UnitTests.Mfa;

/// <summary>
/// Pruebas unitarias para la degradación Fail-Closed del store de Step-Up y Challenge (T-107 / HU-031).
/// </summary>
public class MfaStepUpFailClosedTests
{
    private readonly IConnectionMultiplexer _redisMock;
    private readonly IDatabase _dbMock;
    private readonly IDependencyCircuitBreaker _circuitBreakerMock;

    public MfaStepUpFailClosedTests()
    {
        _redisMock = Substitute.For<IConnectionMultiplexer>();
        _dbMock = Substitute.For<IDatabase>();
        _redisMock.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_dbMock);

        _circuitBreakerMock = Substitute.For<IDependencyCircuitBreaker>();

        // Configurar el mock del CircuitBreaker para ejecutar la lambda provista
        _circuitBreakerMock
            .ExecuteRedisAsync(Arg.Any<Func<Task<ChallengeData?>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<Task<ChallengeData?>>>()());

        _circuitBreakerMock
            .ExecuteRedisAsync(Arg.Any<Func<Task<StepUpData?>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<Task<StepUpData?>>>()());

        _circuitBreakerMock
            .ExecuteRedisAsync(Arg.Any<Func<Task<long>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<Task<long>>>()());

        _circuitBreakerMock
            .ExecuteRedisAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<Task>>()());
    }

    [Fact]
    public async Task ChallengeStore_GetAsync_RedisFails_ShouldExecuteThroughCircuitBreakerAndThrow()
    {
        _dbMock.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis indisponible"));

        var store = new ChallengeStore(_redisMock, _circuitBreakerMock);

        await Assert.ThrowsAsync<RedisConnectionException>(() => store.GetAsync(Guid.NewGuid()));
        await _circuitBreakerMock.Received(1).ExecuteRedisAsync(Arg.Any<Func<Task<ChallengeData?>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StepUpStore_SetAsync_RedisFails_ShouldExecuteThroughCircuitBreakerAndThrow()
    {
        var circuitBreakerMock = Substitute.For<IDependencyCircuitBreaker>();
        circuitBreakerMock.ExecuteRedisAsync(Arg.Any<Func<Task<bool>>>(), Arg.Any<CancellationToken>())
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis indisponible"));
        circuitBreakerMock.ExecuteRedisAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis indisponible"));

        var store = new StepUpStore(_redisMock, circuitBreakerMock);
        var data = new StepUpData("fingerprint_hash", DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<RedisConnectionException>(() => store.SetAsync("user_1", data, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public async Task MfaAttemptStore_IncrementAsync_RedisFails_ShouldExecuteThroughCircuitBreakerAndThrow()
    {
        _dbMock.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis indisponible"));

        var store = new MfaAttemptStore(_redisMock, _circuitBreakerMock);

        await Assert.ThrowsAsync<RedisConnectionException>(() => store.IncrementAsync("user_1", TimeSpan.FromMinutes(15)));
        await _circuitBreakerMock.Received(1).ExecuteRedisAsync(Arg.Any<Func<Task<long>>>(), Arg.Any<CancellationToken>());
    }
}
