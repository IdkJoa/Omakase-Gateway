using Application.Common.RiskEngine.AnomalyDetection;

namespace Infrastructure.Persistence.Caching;

/// <summary>
/// Decorador de <see cref="IUserProfileStore"/> que memoriza el perfil dentro de la misma petición.
/// </summary>
/// <remarks>
/// Evita pedir el mismo perfil dos veces por evaluación (anomalía + penalización de cold-start); sin TTL porque el memo
/// vive solo el ámbito de la petición, así que no hay ventana de obsolescencia. El diccionario no necesita sincronización
/// porque el motor de riesgo evalúa de forma secuencial dentro de una petición.
/// </remarks>
public sealed class RequestScopedUserProfileStore : IUserProfileStore
{
    private readonly IUserProfileStore _inner;
    private readonly Dictionary<string, UserAnomalyProfile?> _memo = new(StringComparer.Ordinal);

    public RequestScopedUserProfileStore(IUserProfileStore inner) => _inner = inner;

    /// <inheritdoc/>
    public async Task<UserAnomalyProfile?> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
            return await _inner.GetAsync(userId, cancellationToken);

        // El "no tiene perfil" también se memoriza: un usuario nuevo pagaba dos búsquedas fallidas.
        if (_memo.TryGetValue(userId, out var cached))
            return cached;

        var profile = await _inner.GetAsync(userId, cancellationToken);
        _memo[userId] = profile;
        return profile;
    }
}
