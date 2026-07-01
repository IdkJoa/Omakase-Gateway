using Application.Common.Mediator;
using Application.Common.RiskEngine;
using Application.Common.RiskEngine.Commands;
using Application.Common.Security;
using Application.Common.Telemetry;
using Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Application.Middlewares;

/// <summary>
/// Middleware de intercepción y extracción de contexto de riesgo (HU-008 / T-015).
/// Se posiciona en el pipeline de YARP inmediatamente antes de <c>MapReverseProxy()</c>,
/// después del <see cref="RateLimitMiddleware"/>.
/// </summary>
/// <remarks>
/// Responsabilidades de esta clase:
/// <list type="number">
///   <item>Extraer el contexto de la petición (IP, User-Agent, cabeceras de fingerprint, UserId).</item>
///   <item>Empaquetar en un <see cref="RequestContext"/> inmutable.</item>
///   <item>Abrir el span OTel <c>gateway.request.intercept</c>.</item>
///   <item>Despachar <see cref="EvaluateRiskCommand"/> al motor de riesgo vía <see cref="IMediator"/>.</item>
///   <item>Actuar sobre el <see cref="Verdict"/> resultante (Block = 403, Allow/Challenge = continuar).</item>
/// </list>
/// La lógica de scoring vive en <see cref="EvaluateRiskHandler"/> y sus sucesores (T-016+).
/// </remarks>
public sealed class RiskEvaluationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RiskEvaluationMiddleware> _logger;

    public RiskEvaluationMiddleware(
        RequestDelegate next,
        ILogger<RiskEvaluationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IMediator mediator,
        ILogSanitizer sanitizer)
    {
        // ── 1. IP de origen ────────────────────────────────────────────────────
        // UseForwardedHeaders() ya procesó X-Forwarded-For antes de llegar aquí (T-019).
        // RemoteIpAddress ya refleja la IP real del cliente.
        var sourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // ── 2. User-Agent sanitizado ───────────────────────────────────────────
        var rawUserAgent = context.Request.Headers.UserAgent.ToString();
        var userAgent = sanitizer.Sanitize(rawUserAgent);

        // ── 3. Cabeceras de fingerprint (Accept-Language, Accept-Encoding) ─────
        var acceptLanguage = context.Request.Headers.AcceptLanguage.ToString();
        var acceptEncoding = context.Request.Headers.AcceptEncoding.ToString();

        // ── 4. UserId del claim JWT (null hasta que se implemente HU-auth) ──────
        var userId = context.User.FindFirst("sub")?.Value;

        // ── 5. Construir RequestContext inmutable 
        var requestContext = new RequestContext
        {
            SourceIp       = sourceIp,
            UserAgent      = string.IsNullOrEmpty(userAgent)      ? null : userAgent,
            UserId         = userId,
            AcceptLanguage = string.IsNullOrEmpty(acceptLanguage) ? null : acceptLanguage,
            AcceptEncoding = string.IsNullOrEmpty(acceptEncoding) ? null : acceptEncoding,
            Timestamp      = DateTimeOffset.UtcNow
        };

        // ── 6. Span OTel 
        using var span = OmakaseActivity.Source.StartActivity(OmakaseActivity.Spans.Interception);
        span?.SetTag(OmakaseActivity.Tags.SourceIp, sourceIp);
        if (userId is not null)
            span?.SetTag(OmakaseActivity.Tags.UserId, userId);

        // ── 7. Despachar al motor de riesgo 
        var result = await mediator.SendAsync(
            new EvaluateRiskCommand(requestContext),
            context.RequestAborted);

        span?.SetTag(OmakaseActivity.Tags.Verdict, result.Verdict.ToString());
        span?.SetTag(OmakaseActivity.Tags.RiskScore, result.RiskScore.ToString("F2"));

        _logger.LogInformation(
            "[RiskEvaluationMiddleware] IP={SourceIp} UA={UserAgent} Verdict={Verdict} Score={Score}",
            sourceIp,
            string.IsNullOrEmpty(userAgent) ? "-" : userAgent,
            result.Verdict,
            result.RiskScore);

        // ── 8. Actuar sobre el veredicto ───────────────────────────────────────
        // Block (Score > 75): cortar con HTTP 403.
        // Challenge (Score 41-75): MFA flow — T-017 implementará la redirección.
        //   Por ahora se deja pasar igual que Allow (handler siempre devuelve Allow en T-015).
        // Allow (Score ≤ 40): reenviar al upstream via YARP.
        if (result.Verdict == Verdict.Block)
        {
            _logger.LogWarning(
                "[RiskEvaluationMiddleware] Petición BLOQUEADA. IP={SourceIp} Score={Score}",
                sourceIp, result.RiskScore);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(new
            {
                errorCode = "ACCESS_DENIED",
                message   = "La petición fue bloqueada por el motor de riesgo.",
                traceId   = context.TraceIdentifier
            });
            return;
        }

        await _next(context);
    }
}