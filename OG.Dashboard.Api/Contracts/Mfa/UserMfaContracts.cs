namespace OG.Dashboard.Api.Contracts.Mfa;

/// <summary>Estado de MFA (TOTP) de un client user para el Dashboard (HU-047 / T-108). EnrollmentPending = secreto provisionado pero aún sin confirmar.</summary>
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
