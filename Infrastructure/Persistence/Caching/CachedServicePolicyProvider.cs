using Application.Common.RiskEngine;
using Domain.Entities;
using Domain.ValueObjects;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Infrastructure.Persistence.Caching;

/// <summary>
/// Decorador de <see cref="IServicePolicyProvider"/> que memoriza el conjunto de políticas por
/// servicio durante <see cref="RiskEngineCacheOptions.ServicePoliciesTtlSeconds"/>.
/// </summary>
/// <remarks>
/// <c>GetByServiceNameAsync</c> hace dos consultas a PostgreSQL por petición sobre tablas que solo cambian por CRUD administrativo;
/// es seguro cachear porque el proveedor consulta con <c>AsNoTracking</c> y el <c>JsonDocument</c> de <c>Config</c> es inmutable en lectura.
/// </remarks>
public sealed class CachedServicePolicyProvider : IServicePolicyProvider
{
    private readonly IServicePolicyProvider _inner;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;

    public CachedServicePolicyProvider(
        IServicePolicyProvider inner, IMemoryCache cache, IOptions<RiskEngineCacheOptions> options)
    {
        _inner = inner;
        _cache = cache;
        _ttl = TimeSpan.FromSeconds(options.Value.ServicePoliciesTtlSeconds);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<AccessPolicy>> GetActivePoliciesAsync(
        ProtectedServiceId serviceId, CancellationToken cancellationToken = default)
    {
        if (_ttl <= TimeSpan.Zero)
            return _inner.GetActivePoliciesAsync(serviceId, cancellationToken);

        return _cache.GetOrCreateAsync($"policies:svc:{serviceId.Value}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _ttl;
            return _inner.GetActivePoliciesAsync(serviceId, cancellationToken);
        })!;
    }

    /// <inheritdoc/>
    public Task<ServicePolicySet?> GetByServiceNameAsync(
        string serviceName, CancellationToken cancellationToken = default)
    {
        if (_ttl <= TimeSpan.Zero)
            return _inner.GetByServiceNameAsync(serviceName, cancellationToken);

        return _cache.GetOrCreateAsync($"policies:name:{serviceName}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _ttl;
            return _inner.GetByServiceNameAsync(serviceName, cancellationToken);
        });
    }
}
