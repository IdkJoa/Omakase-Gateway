namespace Application.Common.RiskEngine.AnomalyDetection;

/// <summary>
/// Puerto de lectura del perfil de comportamiento por usuario (HU-016 / T-033). Abstrae el
/// almacén de dos niveles (Redis caliente → PostgreSQL frío) para que el detector no dependa de la
/// infraestructura y no pegue a disco en la ruta crítica.
/// </summary>
public interface IUserProfileStore
{
    /// <summary>
    /// Recupera el perfil del usuario, o <c>null</c> si aún no tiene historial. La implementación
    /// resuelve primero la caché Redis (<c>profile:{userId}</c>) y cae a PostgreSQL solo en fallo de caché.
    /// </summary>
    Task<UserAnomalyProfile?> GetAsync(string userId, CancellationToken cancellationToken = default);
}
