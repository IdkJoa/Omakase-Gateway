using Application.Common.Audit;
using Application.Common.Mediator;
using Application.Common.RiskEngine;
using Application.Common.RiskEngine.Commands;
using Application.Common.Security;
using Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using static Application.Common.Telemetry.OmakaseActivity;
using static Application.Common.Telemetry.OmakaseActivity.Spans;

namespace Application.Middlewares;

/// <summary>
/// Middleware de intercepción, evaluación de riesgo y despacho de veredicto (HU-008 / HU-037 / T-015 / T-016).
/// Se posiciona en el pipeline de YARP inmediatamente antes de <c>MapReverseProxy()</c>,
/// después del <see cref="RateLimitMiddleware"/>.
/// </summary>
/// <remarks>
/// Responsabilidades:
/// <list type="number">
///   <item>Extraer contexto de la petición (IP, User-Agent, cabeceras de fingerprint, UserId).</item>
///   <item>Empaquetar en un <see cref="RequestContext"/> inmutable.</item>
///   <item>Abrir el span OTel <c>gateway.request.intercept</c>.</item>
///   <item>Despachar <see cref="EvaluateRiskCommand"/> al motor de riesgo vía <see cref="IMediator"/>.</item>
///   <item>Actuar sobre el <see cref="Verdict"/> (Allow → upstream, Challenge → 403 CHALLENGE_REQUIRED, Block → 403 ACCESS_DENIED).</item>
///   <item>Encolar un <see cref="AuditEvent"/> en <see cref="IAuditChannel"/> para persistencia asíncrona (T-100).</item>
/// </list>
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
        ILogSanitizer sanitizer,
        IAuditChannel auditChannel)
    {
        var evaluationId = Guid.NewGuid();

        // IP de origen 
        // UseForwardedHeaders() ya procesó X-Forwarded-For antes de llegar aquí (T-019).
        var sourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // User-Agent sanitizado 
        var userAgent = sanitizer.Sanitize(context.Request.Headers.UserAgent.ToString());

        // Cabeceras de fingerprint 
        var acceptLanguage = context.Request.Headers.AcceptLanguage.ToString();
        var acceptEncoding = context.Request.Headers.AcceptEncoding.ToString();

        // UserId del claim JWT (null hasta HU-auth) 
        var userId = context.User.FindFirst("sub")?.Value;

        // Servicio destino: primer segmento del path (convención HU-009: /{name}/**).
        var serviceName = ExtractServiceName(context.Request.Path);

        // RequestContext inmutable
        var requestContext = new RequestContext
        {
            SourceIp       = sourceIp,
            UserAgent      = string.IsNullOrEmpty(userAgent)      ? null : userAgent,
            UserId         = userId,
            AcceptLanguage = string.IsNullOrEmpty(acceptLanguage) ? null : acceptLanguage,
            AcceptEncoding = string.IsNullOrEmpty(acceptEncoding) ? null : acceptEncoding,
            ServiceName    = serviceName,
            Timestamp      = DateTimeOffset.UtcNow
        };

        // Span OTel
        using var span = Source.StartActivity(Interception);
        span?.SetTag(Tags.SourceIp, sourceIp);
        if (userId is not null)
            span?.SetTag(Tags.UserId, userId);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        // Evaluar riesgo 
        var result = await mediator.SendAsync(
            new EvaluateRiskCommand(requestContext),
            context.RequestAborted);
            
        sw.Stop();

        span?.SetTag(Tags.Verdict,   result.Verdict.ToString());
        span?.SetTag(Tags.RiskScore,  result.RiskScore.ToString("F2"));

        _logger.LogInformation("Evaluación completada {@Context}", new 
        { 
            UserId = userId ?? "anonymous", 
            ServiceId = result.ServiceId?.ToString() ?? "unknown",
            Verdict = result.Verdict.ToString(), 
            RiskScore = result.RiskScore, 
            TraceId = context.TraceIdentifier,
            DurationMs = sw.ElapsedMilliseconds
        });

        // Encolar AuditEvent (fire-and-forget — no bloquea el pipeline)
        var auditEvent = new AuditEvent(
            EvaluationId:    evaluationId,
            SourceIp:        sourceIp,
            UserAgent:       requestContext.UserAgent,
            UserId:          userId,
            Verdict:         result.Verdict,
            RiskScore:       result.RiskScore,
            PolicyScore:     result.PolicyScore,
            AnomalyScore:    result.AnomalyScore,
            TraceId:         context.TraceIdentifier,
            EvaluatedAt:     DateTimeOffset.UtcNow,
            Geo:             result.Geo,
            TriggeredRules:  result.TriggeredRules,
            ServiceId:       result.ServiceId);

        if (!auditChannel.TryWrite(auditEvent))
        {
            _logger.LogWarning(
                "[RiskEvaluationMiddleware] AuditEvent descartado por canal lleno. EvaluationId={EvaluationId}",
                evaluationId);
        }

        // Despacho de veredicto 
        switch (result.Verdict)
        {
            case Verdict.Allow:
                // Score ≤ 40: reenviar al upstream via YARP de forma transparente.
                await _next(context);
                break;

            case Verdict.Challenge:
                // Score 41-75: se requiere verificación adicional (MFA).
                // NOTA HTTP 403: el estándar semántico para "acceso condicionado" sería
                // un 401 (autenticación) o un 302 hacia el endpoint de MFA. Se usa 403
                // en T-016 por especificación de la tarea. T-017 (flujo MFA completo)
                // puede cambiar esto a un redirect 302 → /mfa/challenge cuando el
                // endpoint de desafío esté implementado.
                _logger.LogWarning(
                    "[RiskEvaluationMiddleware] CHALLENGE requerido. EvaluationId={EvaluationId} IP={SourceIp} Score={Score}",
                    evaluationId, sourceIp, result.RiskScore);

                context.Response.StatusCode  = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsJsonAsync(new
                {
                    errorCode    = GatewayErrorCodes.ChallengeRequired,
                    message      = "Se requiere verificación adicional (MFA) para continuar.",
                    evaluationId = evaluationId,
                    traceId      = context.TraceIdentifier
                });
                break;

            case Verdict.Block:
                // Score > 75: acceso denegado sin posibilidad de desafío.
                _logger.LogWarning(
                    "[RiskEvaluationMiddleware] Petición BLOQUEADA. EvaluationId={EvaluationId} IP={SourceIp} Score={Score}",
                    evaluationId, sourceIp, result.RiskScore);

                context.Response.StatusCode  = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsJsonAsync(new
                {
                    errorCode    = GatewayErrorCodes.AccessDenied,
                    message      = "La petición fue bloqueada por el motor de riesgo.",
                    evaluationId = evaluationId,
                    traceId      = context.TraceIdentifier
                });
                break;

            default:
                // Guardia de seguridad: nunca debe alcanzarse con los valores actuales del enum.
                _logger.LogError(
                    "[RiskEvaluationMiddleware] Veredicto desconocido: {Verdict}. EvaluationId={EvaluationId}",
                    result.Verdict, evaluationId);

                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                break;
        }
    }

    /// <summary>
    /// Extrae el nombre del servicio destino del primer segmento del path
    /// (convención HU-009: <c>/{name}/**</c>). Devuelve null si no hay segmento.
    /// </summary>
    private static string? ExtractServiceName(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value))
            return null;

        var segment = value.AsSpan().TrimStart('/');
        var slash = segment.IndexOf('/');
        var name = slash >= 0 ? segment[..slash] : segment;

        return name.IsEmpty ? null : name.ToString();
    }
}