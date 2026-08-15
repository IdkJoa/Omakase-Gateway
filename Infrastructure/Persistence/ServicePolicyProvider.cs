using Application.Common.RiskEngine;
using Domain.Entities;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class ServicePolicyProvider : IServicePolicyProvider
{
    private readonly OmakaseDbContext _db;

    public ServicePolicyProvider(OmakaseDbContext db) => _db = db;

    public async Task<IReadOnlyList<AccessPolicy>> GetActivePoliciesAsync(
        ProtectedServiceId serviceId, CancellationToken cancellationToken = default)
    {
        return await _db.ServicePolicies
            .AsNoTracking()
            .Where(sp => sp.ServiceId == serviceId
                      && sp.IsEnabled
                      && sp.AccessPolicy!.IsActive)
            .Select(sp => sp.AccessPolicy!)
            .ToListAsync(cancellationToken);
    }

    public async Task<ServicePolicySet?> GetByServiceNameAsync(
        string serviceName, CancellationToken cancellationToken = default)
    {
        var service = await _db.ProtectedServices
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Name == serviceName && s.IsActive, cancellationToken);

        if (service is null)
            return null;

        var policies = await GetActivePoliciesAsync(service.Id, cancellationToken);
        return new ServicePolicySet(service.Id, policies, service.RequiresAuth);
    }
}
