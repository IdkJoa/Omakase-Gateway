using Application.Common.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Application.Middlewares;

/// <summary>
/// Middleware para aplicar control de flujo y prevención de abusos (Rate Limiting) por dirección IP.
/// Se ejecuta de forma temprana para cortar peticiones excesivas antes de consumir recursos.
/// </summary>
public sealed class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitMiddleware> _logger;
    private readonly int _limit;
    private readonly TimeSpan _window;

    public RateLimitMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<RateLimitMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _limit = configuration.GetValue<int>("RateLimiting:Limit", 100);
        var windowSeconds = configuration.GetValue<int>("RateLimiting:WindowSeconds", 60);
        _window = TimeSpan.FromSeconds(windowSeconds);
    }

    public async Task InvokeAsync(HttpContext context, IRedisService redisService)
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString();

        // En caso de que no se pueda resolver la IP, se usa un valor genérico
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            ipAddress = "unknown_ip";
        }

        try
        {
            // Incrementar contador en Redis con TTL correspondiente (HU-005 / HU-010)
            long count = await redisService.IncrementRateLimitAsync(ipAddress, _window);

            if (count > _limit)
            {
                _logger.LogWarning("Rate limit excedido para la IP: {IpAddress} (Contador: {Count}/{Limit})", ipAddress, count, _limit);

                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.ContentType = "application/json";

                var errorResponse = new
                {
                    errorCode = "TOO_MANY_REQUESTS",
                    message = $"Se ha superado el límite de peticiones (máximo {_limit} por minuto).",
                    traceId = context.TraceIdentifier
                };

                await context.Response.WriteAsJsonAsync(errorResponse);
                return; // Cortar el pipeline tempranamente
            }
        }
        catch (Exception ex)
        {
            // Regla de Degradación Segura - Módulo 9 (Fail-Closed)
            // Si Redis está caído o inaccesible, se bloquea el tráfico por seguridad y se reporta HTTP 503.
            _logger.LogCritical(ex, "Fallo crítico en Redis al evaluar el rate limit para la IP: {IpAddress}. Aplicando Fail-Closed.", ipAddress);

            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/json";

            var errorResponse = new
            {
                errorCode = "SERVICE_UNAVAILABLE",
                message = "El servicio de seguridad no está disponible debido a fallas en dependencias críticas.",
                traceId = context.TraceIdentifier
            };

            await context.Response.WriteAsJsonAsync(errorResponse);
            return; // Cortar el pipeline por seguridad
        }

        await _next(context);
    }
}
