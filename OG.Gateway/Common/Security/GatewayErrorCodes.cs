namespace Application.Common.Security;

public static class GatewayErrorCodes
{
    public const string ChallengeRequired = "CHALLENGE_REQUIRED";
    public const string AccessDenied = "ACCESS_DENIED";

    /// <summary>SRS §7.5: el servicio destino exige JWT (requires_auth) y la petición llegó sin identidad.</summary>
    public const string AuthenticationRequired = "AUTHENTICATION_REQUIRED";
    public const string TooManyRequests = "TOO_MANY_REQUESTS";
    public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";

    // Step-up MFA (HU-046, contrato SRS §4.1)
    public const string MfaRequired = "MFA_REQUIRED";
    public const string MfaInvalid = "MFA_INVALID";
    public const string MfaExpired = "MFA_EXPIRED";
    public const string AccountLocked = "ACCOUNT_LOCKED";
}
