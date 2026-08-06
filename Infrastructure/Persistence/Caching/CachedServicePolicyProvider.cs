using Application.Common.RiskEngine;
using Domain.Entities;
using Domain.ValueObjects;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Infrastructure.Persistence.Caching;

/// <summary>
/// Decorador de <see cref="IServicePolicyProvider"/> que memoriza el conjunto de políticas por
/// servicio durante <see cref="RiskEngineCacheOptions.ServicePoliciesTtlSeconds"/> (T-072).
/// </summary>
/// <remarks>
/// Es la fase más cara de la evaluación: <b>17,33 ms de media (27,4 % del total)</b> medidos con
/// 1.000 muestras bajo 100 VUs sostenidos, porque <c>GetByServiceNameAsync</c> hace <b>dos</b>
/// consultas a PostgreSQL —el servicio y sus políticas— en cada petición, sobre tablas que cambian
/// solo cuando un administrador toca el CRUD.
/// <para>
/// Open/Closed: no modifica <see cref="ServicePolicyProvider"/>; se registra por delante en el
/// contenedor. Con TTL 0 delega siempre y el comportamiento es idéntico al de antes.
/// </para>
/// <para>
/// Seguro de cachear: el proveedor consulta con <c>AsNoTracking</c>, así que las entidades quedan
/// desacopladas del change tracker y el <c>JsonDocument</c> de <c>Config</c> es independiente del
/// DbContext que lo materializó (inmutable en lectura).
/// </para>
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
