using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Common.Security;
using Infrastructure.Proxy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace OG.Application.UnitTests;

public class ProxyConfigReloaderTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldSubscribeToYarpReloadChannel_OnStartup()
    {
        // Arrange
        var provider = new DatabaseProxyConfigProvider();
        var scopeFactoryMock = Substitute.For<IServiceScopeFactory>();
        var redisServiceMock = Substitute.For<IRedisService>();
        var optionsMock = Substitute.For<IOptions<ProxyReloadOptions>>();
        optionsMock.Value.Returns(new ProxyReloadOptions { IntervalSeconds = 60 });
        var loggerMock = Substitute.For<ILogger<ProxyConfigReloader>>();

        var reloader = new ProxyConfigReloader(
            provider,
            scopeFactoryMock,
            redisServiceMock,
            optionsMock,
            loggerMock);

        using var cts = new CancellationTokenSource();
        
        // Act
        // Invoke ExecuteAsync directly using reflection to avoid BackgroundService lifecycle timings
        var methodInfo = typeof(ProxyConfigReloader).GetMethod("ExecuteAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var executeTask = (Task)methodInfo!.Invoke(reloader, new object[] { cts.Token })!;
        
        await Task.Delay(200);
        await cts.CancelAsync();
        try { await executeTask; } catch (OperationCanceledException) {}

        // Assert
        // Verify that it subscribed to "yarp-reload-channel"
        await redisServiceMock.Received(1).SubscribeAsync("yarp-reload-channel", Arg.Any<Action<string, string>>());
    }
}
