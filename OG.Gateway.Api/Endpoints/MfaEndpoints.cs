using Application.Common.Audit;
using Application.Common.Security;
using Application.Common.Security.Mfa;
using Domain.Entities;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;

namespace OG.Gateway.Api.Endpoints;

/// <summary>
/// Endpoints de step-up MFA para client users (HU-046):
/// <list type="bullet">
///   <item><c>POST /auth/mfa/enroll</c> y <c>POST /auth/mfa/enroll/confirm</c> — provisión del secreto TOTP (T-102).</item>
///   <item><c>POST /auth/challenge/verify</c> — verificación del desafío y apertura de la ventana de step-up (T-104).</item>
/// </list>
/// Estos paths están exentos de la evaluación de riesgo (bypass en RiskEvaluationMiddleware)
/// y NO los consume el Dashboard: son para la aplicación del usuario interceptado (SRS §4.1).
/// <para>
/// El enrolamiento exige el JWT propio del Gateway (HU-019): el usuario se identifica por
/// el claim <c>sub</c> de su sesión — nunca por un username arbitrario del body.
/// La verificación del desafío es anónima por diseño: el challengeId es la credencial.
/// </para>
/// </summary>
public static class MfaEndpoints
{
    public sealed record EnrollConfirmRequest(string Otp);
    public sealed record VerifyChallengeRequest(Guid ChallengeId, string Otp);

    public static IEndpointRouteBuilder MapMfaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");

        // T-102: enrolamiento self-service — requiere la sesión del propio usuario (HU-019).
        group.MapPost("/mfa/enroll", EnrollAsync).RequireAuthorization();
        group.MapPost("/mfa/enroll/confirm", ConfirmEnrollAsync).RequireAuthorization();

        // T-104: verificación del desafío — anónima (el challengeId es la credencial).
        group.MapPost("/challenge/verify", VerifyChallengeAsync).AllowAnonymous();

        return app;
    }

    /// <summary>Resuelve el usuario CLIENT_USER autenticado desde el claim <c>sub</c> del JWT.</summary>
    private static async Task<User?> GetAuthenticatedClientUserAsync(
        HttpContext http, OmakaseDbContext db, CancellationToken ct)
    {
        var sub = http.User.FindFirst("sub")?.Value
               ?? http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(sub, out var guid))
            return null;

        var id = Domain.ValueObjects.UserId.From(guid);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

        return user is { IsActive: true, Type: UserType.Client } ? user : null;
    }

    /// <summary>T-102: genera el secreto TOTP, lo cifra en reposo y devuelve el provisioning URI.</summary>
    private static async Task<IResult> EnrollAsync(
        OmakaseDbContext db,
        ITotpService totp,
        ITotpSecretProtector protector,
        IOptions<MfaOptions> options,
        HttpContext http,
        CancellationToken ct)
    {
        var user = await GetAuthenticatedClientUserAsync(http, db, ct);
        if (user is null)
            return Results.Json(
                new { errorCode = GatewayErrorCodes.MfaInvalid, traceId = http.TraceIdentifier },
                statusCode: StatusCodes.Status401Unauthorized);

        if (user.MfaEnabled)
            return Results.Json(
                new { errorCode = GatewayErrorCodes.MfaInvalid, message = "La cuenta ya tiene MFA activo; use el reset del Dashboard (HU-047).", traceId = http.TraceIdentifier },
                statusCode: StatusCodes.Status409Conflict);

        var secret = totp.GenerateSecret();
        user.TotpSecret = protector.Protect(secret);
        user.MfaEnabled = false; // se activa al confirmar con el primer OTP válido
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var uri = totp.BuildProvisioningUri(options.Value.Issuer, user.Username, secret);
        return Results.Ok(new { provisioningUri = uri });
    }

    /// <summary>T-102: confirma el enrolamiento con el primer OTP válido y activa mfa_enabled.</summary>
    private static async Task<IResult> ConfirmEnrollAsync(
        EnrollConfirmRequest request,
        OmakaseDbContext db,
        ITotpService totp,
        ITotpSecretProtector protector,
        ILoggerFactory loggerFactory,
        HttpContext http,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Otp))
            return ValidationProblem(http, "otp es requerido.");

        var logger = loggerFactory.CreateLogger(nameof(MfaEndpoints));

        var user = await GetAuthenticatedClientUserAsync(http, db, ct);
        if (user is null || user.TotpSecret is null)
            return Results.Json(
                new { errorCode = GatewayErrorCodes.MfaInvalid, traceId = http.TraceIdentifier },
                statusCode: StatusCodes.Status401Unauthorized);

        var secret = TryUnprotect(protector, user.TotpSecret, logger);
        if (secret is null || !totp.ValidateCode(secret, request.Otp, DateTimeOffset.UtcNow))
            return Results.Json(
                new { errorCode = GatewayErrorCodes.MfaInvalid, traceId = http.TraceIdentifier },
                statusCode: StatusCodes.Status401Unauthorized);

        user.MfaEnabled = true;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { status = "enrolled" });
    }

    /// <summary>
    /// T-104: valida el TOTP (±1 paso), aplica uso único del desafío y rate-limit/lockout.
    /// Al éxito fija <c>stepup:{userId}</c> (TTL 10 min, ligado al fingerprint del desafío).
    /// Contrato de respuestas: SRS §4.1 (200 verified / 401 MFA_INVALID|MFA_EXPIRED / 423 ACCOUNT_LOCKED).
    /// </summary>
    private static async Task<IResult> VerifyChallengeAsync(
        VerifyChallengeRequest request,
        OmakaseDbContext db,
        IChallengeStore challenges,
        IStepUpStore stepUps,
        IMfaAttemptStore attempts,
        ITotpService totp,
        ITotpSecretProtector protector,
        IAuditChannel audit,
        IOptions<MfaOptions> options,
        ILogSanitizer sanitizer,
        ILoggerFactory loggerFactory,
        HttpContext http,
        CancellationToken ct)
    {
        var cfg = options.Value;
        var logger = loggerFactory.CreateLogger(nameof(MfaEndpoints));

        if (request.ChallengeId == Guid.Empty || string.IsNullOrWhiteSpace(request.Otp))
            return ValidationProblem(http, "challengeId y otp son requeridos.");

        // Desafío vigente (uso único, TTL corto). Expirado → re-emitir (SRS §3.6).
        var challenge = await challenges.GetAsync(request.ChallengeId);
        if (challenge is null)
            return Results.Json(
                new { errorCode = GatewayErrorCodes.MfaExpired, traceId = http.TraceIdentifier },
                statusCode: StatusCodes.Status401Unauthorized);

        if (!Guid.TryParse(challenge.UserId, out var userGuid))
            return Results.Json(
                new { errorCode = GatewayErrorCodes.MfaInvalid, traceId = http.TraceIdentifier },
                statusCode: StatusCodes.Status401Unauthorized);

        var userId = Domain.ValueObjects.UserId.From(userGuid);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null || !user.IsActive)
            return Results.Json(
                new { errorCode = GatewayErrorCodes.MfaInvalid, traceId = http.TraceIdentifier },
                statusCode: StatusCodes.Status401Unauthorized);

        var now = DateTimeOffset.UtcNow;

        // Cuenta ya bloqueada → 423 con contexto (SRS §3.6).
        if (user.LockedUntil is { } locked && locked > now)
            return AccountLocked(http, locked, now);

        // Rate-limit anti fuerza bruta: el intento se cuenta ANTES de validar.
        var attemptCount = await attempts.IncrementAsync(challenge.UserId, TimeSpan.FromMinutes(cfg.AttemptWindowMinutes));
        if (attemptCount > cfg.MaxAttempts)
        {
            user.LockedUntil = now.AddMinutes(cfg.LockoutMinutes);
            user.UpdatedAt = now;
            await db.SaveChangesAsync(ct);

            EnqueueMfaAudit(audit, challenge, http, sanitizer, MfaRuleNames.Failed, Verdict.Block,
                detail: "cuenta bloqueada por exceso de intentos MFA");

            return AccountLocked(http, user.LockedUntil.Value, now);
        }

        if (!user.MfaEnabled || user.TotpSecret is null)
        {
            EnqueueMfaAudit(audit, challenge, http, sanitizer, MfaRuleNames.Failed, Verdict.Block,
                detail: "cuenta sin TOTP enrolado");
            return Results.Json(
                new { errorCode = GatewayErrorCodes.MfaInvalid, traceId = http.TraceIdentifier },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var secret = TryUnprotect(protector, user.TotpSecret, logger);
        if (secret is null || !totp.ValidateCode(secret, request.Otp, now))
        {
            // AC HU-046: al quinto intento fallido la cuenta se bloquea (HTTP 423)
            // y cada intento se registra como MFA_FAILED.
            if (attemptCount >= cfg.MaxAttempts)
            {
                user.LockedUntil = now.AddMinutes(cfg.LockoutMinutes);
                user.UpdatedAt = now;
                await db.SaveChangesAsync(ct);

                EnqueueMfaAudit(audit, challenge, http, sanitizer, MfaRuleNames.Failed, Verdict.Block,
                    detail: $"cuenta bloqueada al intento MFA fallido nº {attemptCount}");

                return AccountLocked(http, user.LockedUntil.Value, now);
            }

            EnqueueMfaAudit(audit, challenge, http, sanitizer, MfaRuleNames.Failed, Verdict.Block,
                detail: "OTP inválido");
            return Results.Json(
                new { errorCode = GatewayErrorCodes.MfaInvalid, traceId = http.TraceIdentifier },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // Éxito: uso único del desafío, reset del contador y apertura de la ventana de step-up
        // ligada a la huella del dispositivo que originó el desafío (SRS §3.6).
        await challenges.RemoveAsync(request.ChallengeId);
        await attempts.ResetAsync(challenge.UserId);
        await stepUps.SetAsync(
            challenge.UserId,
            new StepUpData(challenge.FingerprintHash, now),
            TimeSpan.FromMinutes(cfg.StepUpTtlMinutes));

        EnqueueMfaAudit(audit, challenge, http, sanitizer, MfaRuleNames.Verified, Verdict.Allow,
            detail: "step-up TOTP verificado");

        return Results.Ok(new { status = "verified" });
    }

    /// <summary>
    /// Descifra el secreto TOTP tolerando un texto cifrado ilegible (clave rotada en Key Vault,
    /// dato corrupto o migrado con otra clave): devuelve null en vez de propagar la excepción
    /// criptográfica. Fail-closed (SRS §9.4): si el secreto no se puede leer, no se puede verificar
    /// el segundo factor, así que se deniega — nunca un 500 que exponga el fallo interno.
    /// </summary>
    private static string? TryUnprotect(ITotpSecretProtector protector, string ciphertext, ILogger logger)
    {
        try
        {
            return protector.Unprotect(ciphertext);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            logger.LogError(ex,
                "[MFA] El secreto TOTP almacenado no se pudo descifrar; se deniega la verificación. " +
                "Revise la clave Mfa:TotpEncryptionKey (¿rotada?) y re-enrole la cuenta.");
            return null;
        }
    }

    private static IResult AccountLocked(HttpContext http, DateTimeOffset lockedUntil, DateTimeOffset now)
        => Results.Json(
            new
            {
                errorCode = GatewayErrorCodes.AccountLocked,
                retryAfterSeconds = (int)Math.Max(0, (lockedUntil - now).TotalSeconds),
                traceId = http.TraceIdentifier
            },
            statusCode: StatusCodes.Status423Locked);

    private static IResult ValidationProblem(HttpContext http, string message)
        => Results.Json(
            new { errorCode = "VALIDATION_ERROR", message, traceId = http.TraceIdentifier },
            statusCode: StatusCodes.Status400BadRequest);

    /// <summary>
    /// Registra el evento MFA en auditoría vía el canal asíncrono (T-100): la escritura
    /// nunca bloquea la respuesta, coherente con RF-M1/§9.8 del SRS.
    /// </summary>
    private static void EnqueueMfaAudit(
        IAuditChannel audit,
        ChallengeData challenge,
        HttpContext http,
        ILogSanitizer sanitizer,
        string rule,
        Verdict verdict,
        string detail)
    {
        var triggered = JsonSerializer.SerializeToDocument(new[]
        {
            new { rule, score = 0m, detail = (string?)detail }
        });

        var userAgent = sanitizer.Sanitize(http.Request.Headers.UserAgent.ToString());

        audit.TryWrite(new AuditEvent(
            EvaluationId:    Guid.NewGuid(),
            SourceIp:        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            UserAgent:       string.IsNullOrEmpty(userAgent) ? null : userAgent,
            UserId:          challenge.UserId,
            Verdict:         verdict,
            RiskScore:       0m,
            PolicyScore:     0m,
            AnomalyScore:    0m,
            TraceId:         http.TraceIdentifier,
            EvaluatedAt:     DateTimeOffset.UtcNow,
            TriggeredRules:  triggered,
            FingerprintHash: challenge.FingerprintHash));
    }
}
