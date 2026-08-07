using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;

namespace Infrastructure.Resilience;

/// <summary>
/// Middleware de protección Fail-Closed (T-067 / HU-031).
/// Intercepta peticiones cuando el Circuit Breaker de Redis o PostgreSQL está ABIERTO,
/// o cuando una dependencia falla durante el procesamiento, retornando HTTP 503 Service Unavailable.
/// </summary>
public sealed class DependencyCircuitBreakerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DependencyCircuitBreakerMiddleware> _logger;

    public DependencyCircuitBreakerMiddleware(RequestDelegate next, ILogger<DependencyCircuitBreakerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IDependencyCircuitBreaker circuitBreaker)
    {
        try
        {
            await _next(context);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError(ex,
                "[FailClosed] Excepción de Circuit Breaker capturada durante la petición. Respondiendo HTTP 503. Ruta: {Path}",
                context.Request.Path);

            await RespondWithServiceUnavailableAsync(context, "Acceso bloqueado: dependencia crítica no respondió (Fail-Closed).");
        }
    }

    private static async Task RespondWithServiceUnavailableAsync(HttpContext context, string detail)
    {
        Activity.Current?.AddEvent(new ActivityEvent("FailClosed_Request_Blocked_503"));

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/problem+json";

            var problemDetails = new
            {
                type = "https://tools.ietf.org/html/rfc7231#section-6.6.4",
                title = "Service Unavailable",
                status = StatusCodes.Status503ServiceUnavailable,
                detail = detail,
                instance = context.Request.Path.Value
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails));
        }
    }
}
