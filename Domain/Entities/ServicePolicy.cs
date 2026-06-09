using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Join entity that resolves the many-to-many relationship between
/// <see cref="ProtectedService"/> and <see cref="AccessPolicy"/>.
/// <para>
/// This granularity allows each service to apply its own set of policies independently.
/// For example, a payroll service may require strict time-window and geofencing rules,
/// while a public-reports service may only apply rate limiting.
/// </para>
/// <para>
/// The combination (<see cref="ServiceId"/>, <see cref="PolicyId"/>) must be unique.
/// If a policy is not associated with any service, it is never evaluated.
/// </para>
/// </summary>
public class ServicePolicy
{
    public ServicePolicyId Id { get; init; }

    // ── Foreign Keys ──────────────────────────────────────────────────────────

    /// <summary>Service to which the policy is applied. CASCADE on service deletion.</summary>
    public ProtectedServiceId ServiceId { get; init; }

    /// <summary>Policy being applied to the service. CASCADE on policy deletion.</summary>
    public AccessPolicyId PolicyId { get; init; }

    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>
    /// Allows activating or deactivating a policy for a specific service
    /// without removing the association.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    // ── Navigation Properties ─────────────────────────────────────────────────

    public ProtectedService? ProtectedService { get; set; }
    public AccessPolicy? AccessPolicy { get; set; }
}
