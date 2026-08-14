using Domain.Entities;
using Domain.ValueObjects;

namespace Application.Common.RiskEngine;

// RequiresAuth (SRS §7.5): when true the service demands a valid JWT before any risk evaluation;
// an anonymous request is denied with 401 up front (never reaches rules/ML).
public sealed record ServicePolicySet(
    ProtectedServiceId ServiceId,
    IReadOnlyList<AccessPolicy> Policies,
    bool RequiresAuth = false);

public interface IServicePolicyProvider
{
    // Active means policy.IsActive AND association.IsEnabled.
    Task<IReadOnlyList<AccessPolicy>> GetActivePoliciesAsync(
        ProtectedServiceId serviceId, CancellationToken cancellationToken = default);

    // Resolves by name via the HU-009 path convention; null when no active service matches.
    Task<ServicePolicySet?> GetByServiceNameAsync(
        string serviceName, CancellationToken cancellationToken = default);
}
