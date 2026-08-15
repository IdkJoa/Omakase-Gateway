using Application.Common.Audit;
using Application.Common.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Application.Common.Options;

namespace Application.Middlewares;

public sealed class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitMiddleware> _logger;
    private readonly int _limit;
    private readonly TimeSpan _window;

    public RateLimitMiddleware(
        RequestDelegate next,
        IOptions<RateLimitingOptions> options,
        ILogger<RateLimitMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
        _limit  = options.Value.Limit;
        _window = TimeSpan.FromSeconds(options.Value.WindowSeconds);
    }

    public async Task InvokeAsync(HttpContext context, IRedisService redisService)
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString();

        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            ipAddress = "unknown_ip";
        }

        try
        {
            long count = await redisService.IncrementRateLimitAsync(ipAddress, _window);

            if (count > _limit)
            {
                _logger.LogWarning(
                    "Rate limit excedido para la IP: {IpAddress} (Contador: {Count}/{Limit})",
                    ipAddress, count, _limit);

                context.Response.StatusCode  = StatusCodes.Status429TooManyRequests;
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsJsonAsync(new
                {
                    errorCode = GatewayErrorCodes.TooManyRequests,
                    message   = $"Se ha superado el límite de peticiones (máximo {_limit} por minuto).",
                    traceId   = context.TraceIdentifier
                });
                return;
            }
        }
        catch (Exception ex)
        {
            // Fail-closed (Módulo 9): si Redis está caído se bloquea el tráfico por seguridad, HTTP 503.
            _logger.LogCritical(
                ex,
                "Fallo crítico en Redis al evaluar el rate limit para la IP: {IpAddress}. Aplicando Fail-Closed.",
                ipAddress);

            context.Response.StatusCode  = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(new
            {
                errorCode = GatewayErrorCodes.ServiceUnavailable,
                message   = "El servicio de seguridad no está disponible debido a fallas en dependencias críticas.",
                traceId   = context.TraceIdentifier
            });
            return;
        }

        await _next(context);
    }
}
