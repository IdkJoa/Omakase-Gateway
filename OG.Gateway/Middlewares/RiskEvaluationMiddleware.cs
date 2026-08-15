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
/// Middleware de intercepción, evaluación de riesgo y despacho de veredicto. Se posiciona en el
/// pipeline de YARP inmediatamente antes de <c>MapReverseProxy()</c>, después de <see cref="RateLimitMiddleware"/>.
/// </summary>
public sealed class RiskEvaluationMiddleware
{
    /// <summary>
    /// Se deriva de <see cref="ProtectedService.ReservedNames"/> para que no pueda divergir de
    /// los nombres que el CRUD administrativo rechaza: es la misma regla vista desde los dos lados.
    /// </summary>
    private static readonly string[] BypassPrefixes = ProtectedService.ReservedPathPrefixes;

    /// <summary>Requisito no funcional: el overhead de evaluación debe quedar en ≤50 ms p95.</summary>
    private const double EvaluationBudgetMs = 50d;

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
        if (IsBypassedPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var evaluationId = Guid.NewGuid();

        // UseForwardedHeaders() ya procesó X-Forwarded-For antes de llegar aquí.
        var sourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var userAgent = sanitizer.Sanitize(context.Request.Headers.UserAgent.ToString());

        var acceptLanguage = context.Request.Headers.AcceptLanguage.ToString();
        var acceptEncoding = context.Request.Headers.AcceptEncoding.ToString();

        // Fallback a ClaimTypes.NameIdentifier por si el mapeo de claims entrantes está activo.
        var userId = context.User.FindFirst("sub")?.Value
                  ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        // Servicio destino: primer segmento del path (convención /{name}/**).
        var serviceName = ExtractServiceName(context.Request.Path);

        // Liga el desafío/step-up al dispositivo y completa el registro de auditoría.
        var fingerprintHash = fingerprintService.GenerateHash(userAgent, acceptLanguage, acceptEncoding);

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

        using var span = Source.StartActivity(Interception);
        span?.SetTag(Tags.SourceIp, sourceIp);
        
        if (userId is not null)
            span?.SetTag(Tags.UserId, userId);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        var result = await mediator.SendAsync(
            new EvaluateRiskCommand(requestContext),
            context.RequestAborted);
            
        sw.Stop();

        span?.SetTag(Tags.Verdict,   result.Verdict.ToString());
        span?.SetTag(Tags.RiskScore,  result.RiskScore.ToString("F2"));

        // DurationMs va en decimales: ElapsedMilliseconds trunca a entero y con evaluaciones reales
        // de 9–11 ms ese truncamiento se come hasta un 10% del valor, justo en el rango que decide
        // si se cumple el SLA de overhead ≤50 ms p95.
        _logger.LogInformation("Evaluación completada {@Context}", new
        {
            UserId = userId ?? "anonymous",
            ServiceId = result.ServiceId?.ToString() ?? "unknown",
            Verdict = result.Verdict.ToString(),
            RiskScore = result.RiskScore,
            TraceId = context.TraceIdentifier,
            DurationMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2)
        });

        // Desglose por fase solo cuando se excede el presupuesto —coste cero en la ruta normal—;
        // es la cola lenta la que interesa para localizar el cuello de botella. Sin este desglose,
        // un p95 fuera de presupuesto solo dice que falla, no dónde.
        if (result.Timings is not null && sw.Elapsed.TotalMilliseconds > EvaluationBudgetMs)
        {
            _logger.LogWarning("Presupuesto de evaluación excedido {@Budget}", new
            {
                TotalMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                BudgetMs = EvaluationBudgetMs,
                ConfigMs = Math.Round(result.Timings.ConfigMs, 2),
                PoliciesMs = Math.Round(result.Timings.PoliciesMs, 2),
                RulesMs = Math.Round(result.Timings.RulesMs, 2),
                GeoCheckMs = Math.Round(result.Timings.GeoCheckMs, 2),
                AnomalyMs = Math.Round(result.Timings.AnomalyMs, 2),
                AccessCountMs = Math.Round(result.Timings.AccessCountMs, 2),
                StepUpMs = Math.Round(result.Timings.StepUpMs, 2),
                AuditBuildMs = Math.Round(result.Timings.AuditBuildMs, 2),
                TraceId = context.TraceIdentifier
            });
        }

        // fire-and-forget — no bloquea el pipeline
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

        // El servicio exige JWT (requires_auth) y la petición no trae identidad → 401, antes
        // del veredicto de riesgo (SRS §7.5).
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

        switch (result.Verdict)
        {
            case Verdict.Allow:
                await _next(context);
                break;

            case Verdict.Challenge:
                // El desafío se persiste en Redis con el contexto de la petición original;
                // el cliente lo completa en POST /auth/challenge/verify (contrato SRS §4.1).
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
                        // Fail-closed (SRS §9.4): sin store de step-up no hay desafío completable
                        // → se deniega con 503, nunca se permite.
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
                // No debe alcanzarse con los valores actuales del enum; guardia de seguridad.
                _logger.LogError(
                    "[RiskEvaluationMiddleware] Veredicto desconocido: {Verdict}. EvaluationId={EvaluationId}",
                    result.Verdict, evaluationId);

                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                break;
        }
    }

    private static bool IsBypassedPath(PathString path)
    {
        foreach (var prefix in BypassPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Primer segmento del path (convención <c>/{name}/**</c>); null si no hay segmento.</summary>
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