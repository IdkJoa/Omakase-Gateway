using Domain.Entities;
using Domain.ValueObjects;

namespace Application.Common.RiskEngine;

/// <summary>The destination service (its id) plus the active policies to evaluate for it.</summary>
/// <param name="ServiceId">Id of the resolved active service.</param>
/// <param name="Policies">Active deterministic policies linked to the service.</param>
/// <param name="RequiresAuth">
/// SRS §7.5: when true the service demands a valid JWT before any risk evaluation; an anonymous
/// request is denied with 401 up front (never reaches rules/ML). Defaults to false (open service).
/// </param>
public sealed record ServicePolicySet(
    ProtectedServiceId ServiceId,
    IReadOnlyList<AccessPolicy> Policies,
    bool RequiresAuth = false);

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

    /// <summary>
    /// Resolves an active service by name (HU-009 path convention) and returns its
    /// id plus active policies. Returns null when no active service matches the name.
    /// </summary>
    Task<ServicePolicySet?> GetByServiceNameAsync(
        string serviceName, CancellationToken cancellationToken = default);
}
