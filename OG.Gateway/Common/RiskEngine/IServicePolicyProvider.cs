using Domain.Entities;
using Domain.ValueObjects;

namespace Application.Common.RiskEngine;

/// <summary>
/// Provides the active deterministic policies associated with a given service
/// (via service_policies). Port implemented in Infrastructure over the DbContext.
/// </summary>
public interface IServicePolicyProvider
{
    /// <summary>
    /// Returns the active policies (policy.IsActive AND association.IsEnabled)
    /// linked to the service. Each service evaluates only its own policies.
    /// </summary>
    Task<IReadOnlyList<AccessPolicy>> GetActivePoliciesAsync(
        ProtectedServiceId serviceId, CancellationToken cancellationToken = default);
}
