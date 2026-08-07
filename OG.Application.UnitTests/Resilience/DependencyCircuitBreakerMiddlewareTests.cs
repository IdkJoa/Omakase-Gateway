using System.IO;
using System.Net;
using Infrastructure.Resilience;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Polly.CircuitBreaker;
using Xunit;

namespace OG.Application.UnitTests.Resilience;

/// <summary>
/// Pruebas unitarias para DependencyCircuitBreakerMiddleware (T-067 / HU-031).
/// Valida la captura de BrokenCircuitException y la respuesta Fail-Closed con HTTP 503.
/// </summary>
public class DependencyCircuitBreakerMiddlewareTests
{
    private readonly IDependencyCircuitBreaker _circuitBreakerMock;
    private readonly ILogger<DependencyCircuitBreakerMiddleware> _loggerMock;
    private bool _nextCalled;
    private readonly RequestDelegate _next;

    public DependencyCircuitBreakerMiddlewareTests()
    {
        _circuitBreakerMock = Substitute.For<IDependencyCircuitBreaker>();
        _loggerMock = Substitute.For<ILogger<DependencyCircuitBreakerMiddleware>>();
        _nextCalled = false;
        _next = (context) =>
        {
            _nextCalled = true;
            return Task.CompletedTask;
        };
    }

    private DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/protected";
        return context;
    }

    [Fact]
    public async Task InvokeAsync_NormalExecution_ShouldCallNextMiddleware()
    {
        // Arrange
        var context = CreateHttpContext();
        var middleware = new DependencyCircuitBreakerMiddleware(_next, _loggerMock);

        // Act
        await middleware.InvokeAsync(context, _circuitBreakerMock);

        // Assert
        Assert.True(_nextCalled);
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_NextThrowsBrokenCircuitException_ShouldCatchAndReturn503()
    {
        // Arrange
        var context = CreateHttpContext();
        RequestDelegate nextThrowing = (ctx) => throw new BrokenCircuitException("Circuit Breaker para Redis está ABIERTO.");
        var middleware = new DependencyCircuitBreakerMiddleware(nextThrowing, _loggerMock);

        // Act
        await middleware.InvokeAsync(context, _circuitBreakerMock);

        // Assert
        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var responseBody = await reader.ReadToEndAsync();
        
        Assert.Contains("Service Unavailable", responseBody);
        Assert.Contains("503", responseBody);
    }
}
