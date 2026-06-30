using Application.Middlewares;
using Application.Common.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas unitarias para verificar el comportamiento de RateLimitMiddleware (HU-010).
/// Valida el flujo bajo el límite, superando el límite (HTTP 429) y con fallas en Redis (HTTP 503 Fail-Closed).
/// </summary>
public class RateLimitMiddlewareTests
{
    private readonly IRedisService _redisMock;
    private readonly IConfiguration _config;
    private readonly ILogger<RateLimitMiddleware> _loggerMock;
    private bool _nextCalled;
    private readonly RequestDelegate _next;

    public RateLimitMiddlewareTests()
    {
        _redisMock = Substitute.For<IRedisService>();
        _loggerMock = Substitute.For<ILogger<RateLimitMiddleware>>();
        _nextCalled = false;
        _next = (context) =>
        {
            _nextCalled = true;
            return Task.CompletedTask;
        };

        // Usamos una configuración real en memoria para evitar problemas de simulación de métodos de extensión.
        var inMemorySettings = new Dictionary<string, string>
        {
            { "RateLimiting:Limit", "100" },
            { "RateLimiting:WindowSeconds", "60" }
        };

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings!)
            .Build();
    }

    private HttpContext CreateHttpContext(string ip)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task InvokeAsync_RequestUnderLimit_ShouldCallNextMiddleware()
    {
        // Arrange
        var context = CreateHttpContext("192.168.1.50");
        _redisMock.IncrementRateLimitAsync("192.168.1.50", Arg.Any<TimeSpan>()).Returns(99);

        var middleware = new RateLimitMiddleware(_next, _config, _loggerMock);

        // Act
        await middleware.InvokeAsync(context, _redisMock);

        // Assert
        Assert.True(_nextCalled);
        Assert.Equal(200, context.Response.StatusCode); // Estado HTTP por defecto es OK (200)
    }

    [Fact]
    public async Task InvokeAsync_RequestOverLimit_ShouldReturnHttp429AndStopPipeline()
    {
        // Arrange
        var context = CreateHttpContext("192.168.1.50");
        _redisMock.IncrementRateLimitAsync("192.168.1.50", Arg.Any<TimeSpan>()).Returns(101);

        var middleware = new RateLimitMiddleware(_next, _config, _loggerMock);

        // Act
        await middleware.InvokeAsync(context, _redisMock);

        // Assert
        Assert.False(_nextCalled);
        Assert.Equal(429, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        string responseBody = await reader.ReadToEndAsync();
        
        var json = JsonDocument.Parse(responseBody);
        Assert.Equal("TOO_MANY_REQUESTS", json.RootElement.GetProperty("errorCode").GetString());
        Assert.NotNull(json.RootElement.GetProperty("message").GetString());
        Assert.NotNull(json.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task InvokeAsync_RedisThrowsException_ShouldApplyFailClosedAndReturnHttp503()
    {
        // Arrange
        var context = CreateHttpContext("192.168.1.50");
        _redisMock.IncrementRateLimitAsync("192.168.1.50", Arg.Any<TimeSpan>()).Throws(new Exception("Redis connection failed."));

        var middleware = new RateLimitMiddleware(_next, _config, _loggerMock);

        // Act
        await middleware.InvokeAsync(context, _redisMock);

        // Assert
        Assert.False(_nextCalled);
        Assert.Equal(503, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        string responseBody = await reader.ReadToEndAsync();
        
        var json = JsonDocument.Parse(responseBody);
        Assert.Equal("SERVICE_UNAVAILABLE", json.RootElement.GetProperty("errorCode").GetString());
        Assert.NotNull(json.RootElement.GetProperty("message").GetString());
        Assert.NotNull(json.RootElement.GetProperty("traceId").GetString());
    }
}
