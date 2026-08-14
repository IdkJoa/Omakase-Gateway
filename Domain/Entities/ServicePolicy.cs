using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Resolves the many-to-many relationship between <see cref="ProtectedService"/> and <see cref="AccessPolicy"/>,
/// so each service can apply its own independent set of policies. (<see cref="ServiceId"/>, <see cref="PolicyId"/>) is unique;
/// a policy with no association is never evaluated.
/// </summary>
public class ServicePolicy
{
    public ServicePolicyId Id { get; init; }
    public ProtectedServiceId ServiceId { get; init; }
    public AccessPolicyId PolicyId { get; init; }

    /// <summary>Toggles the policy for this service without removing the association.</summary>
    public bool IsEnabled { get; set; } = true;

    public ProtectedService? ProtectedService { get; set; }
    public AccessPolicy? AccessPolicy { get; set; }
}
