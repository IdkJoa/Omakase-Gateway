namespace Application.Common.Security.Mfa;

/// <summary>
/// Nombres de las entradas MFA registradas en <c>triggered_rules</c> de la auditoría
/// (HU-046: MFA_SATISFIED en la reanudación, MFA_FAILED en intentos fallidos y la
/// escalada de clientes no interactivos).
/// </summary>
public static class MfaRuleNames
{
    public const string Satisfied = "MFA_SATISFIED";
    public const string Failed = "MFA_FAILED";
    public const string Verified = "MFA_VERIFIED";
    public const string NonInteractiveEscalated = "NON_INTERACTIVE_CHALLENGE_ESCALATED";
}
