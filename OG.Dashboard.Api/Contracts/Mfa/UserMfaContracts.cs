namespace OG.Dashboard.Api.Contracts.Mfa;

/// <summary>
/// Estado de MFA (TOTP) de un client user para el Dashboard (HU-047 / T-108).
/// </summary>
/// <param name="UserId">Identificador del usuario.</param>
/// <param name="Username">Nombre de usuario (para mostrar en el perfil).</param>
/// <param name="MfaEnabled">True si el segundo factor TOTP está activo y confirmado.</param>
/// <param name="EnrollmentPending">
/// True si hay un secreto provisionado pero aún sin confirmar (enrolamiento a medias).
/// </param>
/// <param name="Locked">True si la cuenta está bloqueada por intentos MFA (HU-046) en este momento.</param>
/// <param name="LockedUntil">Instante hasta el que dura el bloqueo, o null si no está bloqueada.</param>
public sealed record MfaStatusDto(
    Guid UserId,
    string Username,
    bool MfaEnabled,
    bool EnrollmentPending,
    bool Locked,
    DateTimeOffset? LockedUntil);

/// <summary>
/// Respuesta del inicio de enrolamiento: el provisioning URI <c>otpauth://</c> que el front
/// (T-109) renderiza como código QR. El secreto va embebido en el URI; no se expone aparte.
/// </summary>
public sealed record MfaEnrollmentResponse(string ProvisioningUri);

/// <summary>Cuerpo de la confirmación de enrolamiento: el primer OTP de seis dígitos.</summary>
public sealed record MfaEnrollConfirmRequest(string Otp);
