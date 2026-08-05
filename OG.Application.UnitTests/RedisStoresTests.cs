using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Application.Common.Security;
using Infrastructure.Redis;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas unitarias para validar el servicio unificado IRedisService (HU-005 & T-012).
/// Valida que se invoquen los comandos correctos de Redis y se apliquen los TTLs exigidos.
/// </summary>
public class RedisStoresTests
{
    private readonly IConnectionMultiplexer _redisMock;
    private readonly IDatabase _dbMock;

    public RedisStoresTests()
    {
        _redisMock = Substitute.For<IConnectionMultiplexer>();
        _dbMock = Substitute.For<IDatabase>();
        _redisMock.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_dbMock);
    }

    [Fact]
    public async Task RedisService_AddToBlacklistAsync_ShouldCallStringSetWithCorrectTtl()
    {
        // Arrange
        var service = new RedisService(_redisMock);
        string jti = "test-jti-123";
        var ttl = TimeSpan.FromMinutes(8);
        string expectedKey = RedisKeyHelper.GetBlacklistKey(jti);

        // Act
        await service.AddToBlacklistAsync(jti, ttl);

        // Assert
        await _dbMock.Received(1).StringSetAsync(
            expectedKey,
            (RedisValue)"revoked",
            ttl,
            Arg.Any<bool>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RedisService_IsBlacklistedAsync_ShouldCheckKeyExistence()
    {
        // Arrange
        var service = new RedisService(_redisMock);
        string jti = "test-jti-123";
        string expectedKey = RedisKeyHelper.GetBlacklistKey(jti);
        _dbMock.KeyExistsAsync(expectedKey, Arg.Any<CommandFlags>()).Returns(true);

        // Act
        bool isBlacklisted = await service.IsBlacklistedAsync(jti);

        // Assert
        Assert.True(isBlacklisted);
        await _dbMock.Received(1).KeyExistsAsync(expectedKey, Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RedisService_CacheProfileAsync_ShouldCallStringSetWithCorrectTtl()
    {
        // Arrange
        var service = new RedisService(_redisMock);
        string userId = "usr_test123";
        string profileJson = "{\"score\": 12}";
        var ttl = TimeSpan.FromHours(1);
        string expectedKey = RedisKeyHelper.GetProfileKey(userId);

        // Act
        await service.CacheProfileAsync(userId, profileJson, ttl);

        // Assert
        await _dbMock.Received(1).StringSetAsync(
            expectedKey,
            (RedisValue)profileJson,
            ttl,
            Arg.Any<bool>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RedisService_StoreFingerprintAsync_ShouldCallSetAddAndSetTtl()
    {
        // Arrange
        var service = new RedisService(_redisMock);
        string userId = "usr_test123";
        string fingerprint = "hash_browser_abc";
        var ttl = TimeSpan.FromHours(24);
        string expectedKey = RedisKeyHelper.GetFingerprintKey(userId);

        // Act
        await service.StoreFingerprintAsync(userId, fingerprint, ttl);

        // Assert
        await _dbMock.Received(1).SetAddAsync(
            Arg.Is<RedisKey>(k => k == expectedKey),
            Arg.Is<RedisValue>(v => v == fingerprint),
            Arg.Any<CommandFlags>());
        await _dbMock.Received(1).KeyExpireAsync((RedisKey)expectedKey, (TimeSpan?)ttl, ExpireWhen.Always, CommandFlags.None);
    }

    [Fact]
    public async Task RedisService_GetFingerprintsAsync_ShouldReturnListFromSet()
    {
        // Arrange
        var service = new RedisService(_redisMock);
        string userId = "usr_test123";
        string expectedKey = RedisKeyHelper.GetFingerprintKey(userId);
        
        _dbMock.SetMembersAsync(expectedKey, Arg.Any<CommandFlags>()).Returns(new RedisValue[]
        {
            "fingerprint1",
            "fingerprint2"
        });

        // Act
        var result = await service.GetFingerprintsAsync(userId);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("fingerprint1", result);
        Assert.Contains("fingerprint2", result);
    }

    [Fact]
    public async Task RedisService_IncrementRateLimitAsync_ShouldCallStringIncrementAndSetTtlOnFirstCall()
    {
        // Arrange
        var service = new RedisService(_redisMock);
        string ip = "192.168.1.1";
        var window = TimeSpan.FromSeconds(60);
        string expectedKey = RedisKeyHelper.GetRateLimitKey(ip);
        
        _dbMock.StringIncrementAsync(
            expectedKey,
            Arg.Any<long>(),
            Arg.Any<CommandFlags>()).Returns(1);

        // Act
        long count = await service.IncrementRateLimitAsync(ip, window);

        // Assert
        Assert.Equal(1, count);
        await _dbMock.Received(1).StringIncrementAsync(
            Arg.Is<RedisKey>(k => k == expectedKey),
            Arg.Any<long>(),
            Arg.Any<CommandFlags>());
        await _dbMock.Received(1).KeyExpireAsync((RedisKey)expectedKey, (TimeSpan?)window, ExpireWhen.Always, CommandFlags.None);
    }

}
