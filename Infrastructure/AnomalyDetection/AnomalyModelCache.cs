using System.Collections.Concurrent;

namespace Infrastructure.AnomalyDetection;

/// <summary>
/// Caché de modelos de anomalías entrenados, por usuario y viva a nivel de proceso (HU-016 / T-032).
/// Se registra como <b>Singleton</b> para que los modelos persistan entre peticiones, mientras el
/// detector puede seguir siendo Scoped (evita el <i>captive dependency</i> con el <c>DbContext</c>).
/// </summary>
public interface IAnomalyModelCache
{
    /// <summary>Devuelve el modelo del usuario, construyéndolo una sola vez con <paramref name="factory"/>.</summary>
    AnomalyModel GetOrBuild(string userId, Func<AnomalyModel> factory);

    /// <summary>Descarta el modelo del usuario para forzar su reconstrucción.</summary>
    void Invalidate(string userId);
}

/// <inheritdoc cref="IAnomalyModelCache"/>
public sealed class AnomalyModelCache : IAnomalyModelCache
{
    // Lazy<T> garantiza que el entrenamiento (costoso) corra una sola vez aun bajo concurrencia.
    private readonly ConcurrentDictionary<string, Lazy<AnomalyModel>> _cache = new();

    public AnomalyModel GetOrBuild(string userId, Func<AnomalyModel> factory) =>
        _cache.GetOrAdd(userId, _ => new Lazy<AnomalyModel>(factory)).Value;

    public void Invalidate(string userId) => _cache.TryRemove(userId, out _);
}
