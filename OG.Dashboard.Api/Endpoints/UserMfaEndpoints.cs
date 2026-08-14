using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using OG.Dashboard.Api.Contracts.Common;
using OG.Dashboard.Api.Contracts.Mfa;
using OG.Dashboard.Features.Mfa;

namespace OG.Dashboard.Api.Endpoints;

/// <summary>
/// Gestión de MFA (TOTP) por administrador desde el Dashboard (HU-047 / T-108) bajo
/// <c>/api/v1/users/{id}/mfa</c>: ver estado, iniciar/confirmar enrolamiento y resetear el
/// segundo factor de un client user. Endpoint dedicado, separado de <c>UsersEndpoints</c> (SRP).
/// <para>
/// La lógica MFA vive en <see cref="MfaAdminService"/> (reutiliza los primitivos de HU-046); aquí
/// solo quedan las responsabilidades HTTP: cargar la entidad, guardas de estado, persistir y loguear.
/// </para>
/// <para>
/// Autorización: <c>AdminOnly</c> (rol ADMIN) para todo — la gestión del segundo factor es una
/// operación de Security Officer (criterios de aceptación de la HU). El estado del secreto TOTP
/// nunca se devuelve; solo indicadores booleanos.
/// </para>
/// </summary>
public static class UserMfaEndpoints
{
    public static IEndpointRouteBuilder MapUserMfaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users/{id:guid}/mfa")
            .RequireAuthorization("AdminOnly")
            .WithTags("UserMfa")
            .WithOpenApi();

        group.MapGet("", GetStatus)
            .WithName("GetUserMfaStatus")
            .WithSummary("Ver el estado de MFA (TOTP) de un client user")
            .Produces<MfaStatusDto>(StatusCodes.Status200OK);

        group.MapPost("/enroll", BeginEnroll)
            .WithName("BeginUserMfaEnrollment")
            .WithSummary("Iniciar el enrolamiento TOTP: devuelve el provisioning URI para el QR")
            .Produces<MfaEnrollmentResponse>(StatusCodes.Status200OK);

        group.MapPost("/enroll/confirm", ConfirmEnroll)
            .WithName("ConfirmUserMfaEnrollment")
            .WithSummary("Confirmar el enrolamiento con el primer OTP válido (activa mfa_enabled)")
            .Produces<MfaStatusDto>(StatusCodes.Status200OK);

        group.MapPost("/reset", Reset)
            .WithName("ResetUserMfa")
            .WithSummary("Resetear el segundo factor TOTP del usuario")
            .Produces<MfaStatusDto>(StatusCodes.Status200OK);

        return app;
    }

    /// <summary>Estado MFA (solo indicadores; nunca el secreto). Lectura sin tracking.</summary>
    private static async Task<IResult> GetStatus(Guid id, OmakaseDbContext db, CancellationToken ct)
    {
        var userId = UserId.From(id);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return UserNotFound(id);

        return Results.Ok(ToStatus(user));
    }

    /// <summary>
    /// Inicia el enrolamiento sobre un client user activo y devuelve el provisioning URI.
    /// 409 si el usuario no es un client activo (el TOTP no aplica a administradores) o si ya
    /// tiene MFA activo (debe resetearse antes de re-enrolar).
    /// </summary>
    private static async Task<IResult> BeginEnroll(
        Guid id, OmakaseDbContext db, MfaAdminService mfa, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var userId = UserId.From(id);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return UserNotFound(id);

        if (user.Type != UserType.Client || !user.IsActive)
            return Results.Conflict(new ErrorResponse("MFA_NOT_APPLICABLE",
                "El enrolamiento TOTP solo aplica a client users activos.", TraceId()));

        if (user.MfaEnabled)
            return Results.Conflict(new ErrorResponse("MFA_ALREADY_ENABLED",
                "La cuenta ya tiene MFA activo; resetéelo antes de re-enrolar.", TraceId()));

        var provisioningUri = mfa.BeginEnrollment(user);
        await db.SaveChangesAsync(ct);

        loggerFactory.CreateLogger(nameof(UserMfaEndpoints))
            .LogInformation("[HU-047] Enrolamiento MFA iniciado para el usuario {UserId} por un administrador.", user.Id.Value);

        return Results.Ok(new MfaEnrollmentResponse(provisioningUri));
    }

    /// <summary>
    /// Confirma el enrolamiento con el primer OTP. 400 sin OTP, 409 sin secreto pendiente,
    /// 422 si el OTP no valida (fail-closed). Al éxito, MFA queda activo.
    /// </summary>
    private static async Task<IResult> ConfirmEnroll(
        Guid id, MfaEnrollConfirmRequest request, OmakaseDbContext db, MfaAdminService mfa,
        ILoggerFactory loggerFactory, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Otp))
            return Results.BadRequest(new ErrorResponse("VALIDATION_ERROR", "otp es requerido.", TraceId()));

        var userId = UserId.From(id);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return UserNotFound(id);

        if (user.TotpSecret is null)
            return Results.Conflict(new ErrorResponse("MFA_NOT_PENDING",
                "No hay un enrolamiento pendiente; inicie el enrolamiento primero.", TraceId()));

        var logger = loggerFactory.CreateLogger(nameof(UserMfaEndpoints));

        if (!mfa.ConfirmEnrollment(user, request.Otp, DateTimeOffset.UtcNow))
        {
            logger.LogWarning("[HU-047] OTP de confirmación MFA inválido para el usuario {UserId}.", user.Id.Value);
            return Results.Json(new ErrorResponse("MFA_INVALID", "El OTP no es válido.", TraceId()),
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("[HU-047] MFA activado (enrolamiento confirmado) para el usuario {UserId}.", user.Id.Value);

        return Results.Ok(ToStatus(user));
    }

    /// <summary>Resetea el segundo factor (limpia el secreto y desactiva MFA).</summary>
    private static async Task<IResult> Reset(
        Guid id, OmakaseDbContext db, MfaAdminService mfa, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var userId = UserId.From(id);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return UserNotFound(id);

        mfa.Reset(user);
        await db.SaveChangesAsync(ct);

        loggerFactory.CreateLogger(nameof(UserMfaEndpoints))
            .LogInformation("[HU-047] MFA reseteado para el usuario {UserId} por un administrador.", user.Id.Value);

        return Results.Ok(ToStatus(user));
    }

    private static MfaStatusDto ToStatus(User user)
    {
        var locked = user.LockedUntil is { } until && until > DateTimeOffset.UtcNow;
        return new MfaStatusDto(
            user.Id.Value,
            user.Username,
            user.MfaEnabled,
            EnrollmentPending: !user.MfaEnabled && user.TotpSecret is not null,
            locked,
            locked ? user.LockedUntil : null);
    }

    private static IResult UserNotFound(Guid id) =>
        Results.NotFound(new ErrorResponse("NOT_FOUND", $"Usuario '{id}' no encontrado.", TraceId()));

    private static string TraceId() =>
        System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "N/A";
}
