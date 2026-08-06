using Application.Common.Security.Mfa;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Infrastructure.Persistence.Caching;

/// <summary>
/// Decorador de <see cref="IUserMfaInfoProvider"/> que memoriza el estado MFA por usuario durante
/// <see cref="RiskEngineCacheOptions.UserMfaInfoTtlSeconds"/> (T-072).
/// </summary>
/// <remarks>
/// Este proveedor consulta la tabla <c>users</c> y solo se invoca cuando el veredicto es CHALLENGE
/// (SRS §3.6), lo que le da una propiedad indeseable en un gateway de seguridad: <b>cuanto más
/// desafía el sistema, más carga genera contra la base de datos</b>. Un ataque por volumen —que por
/// definición produce desafíos— se convierte así en un amplificador. Medido bajo 100 VUs
/// sostenidos, donde el 99,9 % de las peticiones resolvieron CHALLENGE, la fase de step-up costó
/// <b>16,28 ms de media (25,7 % del total)</b>.
/// <para>
/// Los campos cacheados (<c>IsInteractive</c>, <c>MfaEnabled</c>) cambian con el enrolamiento, no
/// con el tráfico. El TTL acota a 5 s el retardo con que se ve un cambio de enrolamiento; con TTL 0
/// se recupera la consulta directa.
/// </para>
/// </remarks>
public sealed class CachedUserMfaInfoProvider : IUserMfaInfoProvider
{
    private readonly IUserMfaInfoProvider _inner;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;

    public CachedUserMfaInfoProvider(
        IUserMfaInfoProvider inner, IMemoryCache cache, IOptions<RiskEngineCacheOptions> options)
    {
        _inner = inner;
        _cache = cache;
        _ttl = TimeSpan.FromSeconds(options.Value.UserMfaInfoTtlSeconds);
    }

    /// <inheritdoc/>
    public Task<UserMfaInfo?> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (_ttl <= TimeSpan.Zero)
            return _inner.GetAsync(userId, cancellationToken);

        return _cache.GetOrCreateAsync($"mfa-info:{userId}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _ttl;
            return _inner.GetAsync(userId, cancellationToken);
        });
    }
}
