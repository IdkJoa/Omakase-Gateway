using Application.Common.RiskEngine.AnomalyDetection;

namespace Infrastructure.Persistence.Caching;

/// <summary>
/// Decorador de <see cref="IUserProfileStore"/> que memoriza el perfil <b>dentro de la misma
/// petición</b> (T-072).
/// </summary>
/// <remarks>
/// Una sola evaluación pedía el perfil del usuario <b>dos veces</b>: una dentro de
/// <c>RandomizedPcaAnomalyDetector</c> para puntuar la anomalía y otra en
/// <c>EvaluateRiskHandler.ResolveAccessCountAsync</c> para la penalización de cold-start. Es la
/// misma fila, en la misma petición, resuelta por separado. Medido con 1.000 muestras bajo 100 VUs
/// sostenidos: <c>AccessCountMs</c> 10,65 ms y <c>AnomalyMs</c> 10,57 ms, de los que la mayor parte
/// es ese acceso duplicado a Redis (con caída a PostgreSQL si la caché falla).
/// <para>
/// A diferencia de las demás cachés de esta carpeta, aquí <b>no hay TTL ni ventana de obsolescencia
/// posible</b>: el memo vive lo que vive el ámbito de la petición y se descarta con él, así que la
/// segunda lectura devuelve exactamente lo que habría devuelto la consulta. Es eliminar trabajo
/// repetido, no relajar la frescura del dato.
/// </para>
/// <para>
/// Open/Closed: <see cref="UserProfileStore"/> queda intacto; este decorador se registra por delante.
/// El motor de riesgo evalúa de forma secuencial dentro de una petición, así que el diccionario no
/// necesita sincronización.
/// </para>
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
