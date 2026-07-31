using Application.Common.Audit;
using Application.Common.Mediator;
using Application.Common.RiskEngine;
using Application.Common.RiskEngine.Commands;
using Application.Common.Security;
using Application.Common.Security.Mfa;
using Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
///   <item>Actuar sobre el <see cref="Verdict"/> (Allow → upstream, Challenge → 401 MFA_REQUIRED + challengeId (HU-046/T-103), Block → 403 ACCESS_DENIED).</item>
///   <item>Encolar un <see cref="AuditEvent"/> en <see cref="IAuditChannel"/> para persistencia asíncrona (T-100).</item>
/// </list>
/// </remarks>
public sealed class RiskEvaluationMiddleware
{
    /// <summary>
    /// Rutas propias del Gateway que NO se evalúan ni se proxean: endpoints de
    /// autenticación/MFA (HU-046) y diagnóstico de Aspire. Todo lo demás pasa
    /// por el motor de riesgo.
    /// </summary>
    private static readonly string[] BypassPrefixes = ["/auth", "/health", "/alive", "/openapi"];

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
        IAuditChannel auditChannel,
        IFingerprintService fingerprintService,
        IChallengeStore challengeStore,
        IOptions<MfaOptions> mfaOptions)
    {
        // Endpoints propios del Gateway: fuera de la ruta de evaluación (T-103).
        if (IsBypassedPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var evaluationId = Guid.NewGuid();

        // IP de origen 
        // UseForwardedHeaders() ya procesó X-Forwarded-For antes de llegar aquí (T-019).
        var sourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // User-Agent sanitizado 
        var userAgent = sanitizer.Sanitize(context.Request.Headers.UserAgent.ToString());

        // Cabeceras de fingerprint 
        var acceptLanguage = context.Request.Headers.AcceptLanguage.ToString();
        var acceptEncoding = context.Request.Headers.AcceptEncoding.ToString();

        // UserId del claim "sub" del JWT propio (HU-019), ya validado por UseAuthentication.
        // Fallback a ClaimTypes.NameIdentifier por si el mapeo de claims entrantes está activo.
        var userId = context.User.FindFirst("sub")?.Value
                  ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        // Servicio destino: primer segmento del path (convención HU-009: /{name}/**).
        var serviceName = ExtractServiceName(context.Request.Path);

        // Huella del dispositivo (HU-013): liga el desafío/step-up al dispositivo (HU-046)
        // y completa el registro de auditoría (fingerprint_hash).
        var fingerprintHash = fingerprintService.GenerateHash(userAgent, acceptLanguage, acceptEncoding);

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
            ServiceId:       result.ServiceId,
            FingerprintHash: fingerprintHash);

        if (!auditChannel.TryWrite(auditEvent))
        {
            _logger.LogWarning(
                "[RiskEvaluationMiddleware] AuditEvent descartado por canal lleno. EvaluationId={EvaluationId}",
                evaluationId);
        }

        // Precondición de autenticación (SRS §7.5 / HU-024): el servicio exige JWT (requires_auth) y
        // la petición no trae identidad → 401 AUTHENTICATION_REQUIRED, antes del veredicto de riesgo.
        if (result.AuthenticationRequired)
        {
            _logger.LogWarning(
                "[RiskEvaluationMiddleware] Autenticación requerida (requires_auth) sin identidad. EvaluationId={EvaluationId} Servicio={Service}",
                evaluationId, serviceName ?? "unknown");

            context.Response.StatusCode  = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                errorCode    = GatewayErrorCodes.AuthenticationRequired,
                message      = "El servicio solicitado requiere autenticación.",
                evaluationId = evaluationId,
                traceId      = context.TraceIdentifier
            });
            return;
        }

        // Despacho de veredicto
        switch (result.Verdict)
        {
            case Verdict.Allow:
                // Score ≤ 40: reenviar al upstream via YARP de forma transparente.
                await _next(context);
                break;

            case Verdict.Challenge:
                // Score en zona de desafío: HTTP 401 con descriptor MFA_REQUIRED y
                // challengeId (HU-046 / T-103, contrato SRS §4.1). El desafío se
                // persiste en Redis con el contexto de la petición original y el
                // cliente lo completa en POST /auth/challenge/verify (T-104).
                _logger.LogWarning(
                    "[RiskEvaluationMiddleware] CHALLENGE requerido. EvaluationId={EvaluationId} IP={SourceIp} Score={Score}",
                    evaluationId, sourceIp, result.RiskScore);

                Guid? challengeId = null;

                if (userId is not null)
                {
                    challengeId = Guid.NewGuid();
                    var challenge = new ChallengeData(
                        UserId:          userId,
                        ServiceName:     serviceName,
                        FingerprintHash: fingerprintHash,
                        SourceIp:        sourceIp,
                        CreatedAt:       DateTimeOffset.UtcNow);

                    try
                    {
                        await challengeStore.StoreAsync(
                            challengeId.Value,
                            challenge,
                            TimeSpan.FromSeconds(mfaOptions.Value.ChallengeTtlSeconds));
                    }
                    catch (Exception ex)
                    {
                        // Fail-closed (SRS §9.4 / RF-M9): sin store de step-up no hay
                        // desafío completable → se deniega con 503, nunca se permite.
                        _logger.LogError(ex,
                            "[RiskEvaluationMiddleware] Redis no disponible al persistir el desafío. EvaluationId={EvaluationId}",
                            evaluationId);

                        context.Response.StatusCode  = StatusCodes.Status503ServiceUnavailable;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(new
                        {
                            errorCode = GatewayErrorCodes.ServiceUnavailable,
                            message   = "No es posible emitir el desafío de verificación en este momento.",
                            traceId   = context.TraceIdentifier
                        });
                        break;
                    }
                }

                context.Response.StatusCode  = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsJsonAsync(new
                {
                    errorCode    = GatewayErrorCodes.MfaRequired,
                    message      = "Se requiere verificación adicional (MFA) para continuar.",
                    challengeId  = challengeId,
                    methods      = new[] { "totp" },
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

    /// <summary>True si el path es un endpoint propio del Gateway exento de evaluación.</summary>
    private static bool IsBypassedPath(PathString path)
    {
        foreach (var prefix in BypassPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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