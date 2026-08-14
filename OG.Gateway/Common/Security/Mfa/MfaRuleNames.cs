namespace Application.Common.Security.Mfa;

// Nombres de las entradas MFA registradas en triggered_rules de la auditoría.
public static class MfaRuleNames
{
    public const string Satisfied = "MFA_SATISFIED";
    public const string Failed = "MFA_FAILED";
    public const string Verified = "MFA_VERIFIED";
    public const string NonInteractiveEscalated = "NON_INTERACTIVE_CHALLENGE_ESCALATED";
}
