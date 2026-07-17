using System;
using System.Threading.Tasks;
using Application.Common.Security.Mfa;
using Infrastructure.Redis;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas de los stores Redis del step-up MFA (HU-046):
/// challenge:{id}, stepup:{userId} y mfaattempts:{userId} (SRS §7.11.2).
/// Mismo patrón de mocks que RedisStoresTests (HU-005).
/// </summary>
public class MfaStoresTests
{
    private readonly IConnectionMultiplexer _redisMock;
    private readonly IDatabase _dbMock;

    public MfaStoresTests()
    {
        _redisMock = Substitute.For<IConnectionMultiplexer>();
        _dbMock = Substitute.For<IDatabase>();
        _redisMock.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_dbMock);
    }

    private static ChallengeData Challenge() => new(
        UserId: Guid.NewGuid().ToString(),
        ServiceName: "nominas",
        FingerprintHash: "abc123",
        SourceIp: "190.166.12.45",
        CreatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task ChallengeStore_StoreAsync_WritesJsonWithTtl()
    {
        var sut = new ChallengeStore(_redisMock);
        var id = Guid.NewGuid();
        var ttl = TimeSpan.FromMinutes(3);

        await sut.StoreAsync(id, Challenge(), ttl);

        await _dbMock.Received(1).StringSetAsync(
            (RedisKey)$"challenge:{id:D}",
            Arg.Is<RedisValue>(v => v.ToString().Contains("nominas")),
            ttl,
            Arg.Any<bool>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task ChallengeStore_StoreAsync_NonPositiveTtl_Throws()
    {
        var sut = new ChallengeStore(_redisMock);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => sut.StoreAsync(Guid.NewGuid(), Challenge(), TimeSpan.Zero));
    }

    [Fact]
    public async Task ChallengeStore_GetAsync_MissingKey_ReturnsNull()
    {
        _dbMock.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
               .Returns(RedisValue.Null);

        var sut = new ChallengeStore(_redisMock);

        Assert.Null(await sut.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ChallengeStore_GetAsync_RoundTripsData()
    {
        var data = Challenge();
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        _dbMock.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
               .Returns((RedisValue)json);

        var sut = new ChallengeStore(_redisMock);
        var result = await sut.GetAsync(Guid.NewGuid());

        Assert.Equal(data, result);
    }

    [Fact]
    public async Task ChallengeStore_RemoveAsync_DeletesKey_SingleUse()
    {
        var sut = new ChallengeStore(_redisMock);
        var id = Guid.NewGuid();

        await sut.RemoveAsync(id);

        await _dbMock.Received(1).KeyDeleteAsync((RedisKey)$"challenge:{id:D}", Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task StepUpStore_SetAsync_WritesWithTtl()
    {
        var sut = new StepUpStore(_redisMock);
        var ttl = TimeSpan.FromMinutes(10);

        await sut.SetAsync("user-1", new StepUpData("hash", DateTimeOffset.UtcNow), ttl);

        await _dbMock.Received(1).StringSetAsync(
            (RedisKey)"stepup:user-1",
            Arg.Any<RedisValue>(),
            ttl,
            Arg.Any<bool>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task StepUpStore_GetAsync_MissingKey_ReturnsNull()
    {
        _dbMock.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
               .Returns(RedisValue.Null);

        var sut = new StepUpStore(_redisMock);

        Assert.Null(await sut.GetAsync("user-1"));
    }

    [Fact]
    public async Task MfaAttemptStore_FirstIncrement_SetsWindowTtl()
    {
        _dbMock.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
               .Returns(1L);

        var sut = new MfaAttemptStore(_redisMock);
        var window = TimeSpan.FromMinutes(15);

        var count = await sut.IncrementAsync("user-1", window);

        Assert.Equal(1, count);
        await _dbMock.Received(1).KeyExpireAsync(
            (RedisKey)"mfaattempts:user-1", (TimeSpan?)window, ExpireWhen.Always, CommandFlags.None);
    }

    [Fact]
    public async Task MfaAttemptStore_SubsequentIncrement_DoesNotResetTtl()
    {
        _dbMock.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
               .Returns(3L);

        var sut = new MfaAttemptStore(_redisMock);

        var count = await sut.IncrementAsync("user-1", TimeSpan.FromMinutes(15));

        Assert.Equal(3, count);
        await _dbMock.DidNotReceive().KeyExpireAsync(
            Arg.Any<RedisKey>(), Arg.Any<TimeSpan?>(), Arg.Any<ExpireWhen>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task MfaAttemptStore_ResetAsync_DeletesCounter()
    {
        var sut = new MfaAttemptStore(_redisMock);

        await sut.ResetAsync("user-1");

        await _dbMock.Received(1).KeyDeleteAsync((RedisKey)"mfaattempts:user-1", Arg.Any<CommandFlags>());
    }
}
