namespace Application.Common.RiskEngine.AnomalyDetection;

// Abstrae el almacén de dos niveles (Redis caliente -> PostgreSQL frío) para que el detector no
// dependa de la infraestructura ni pegue a disco en la ruta crítica.
public interface IUserProfileStore
{
    Task<UserAnomalyProfile?> GetAsync(string userId, CancellationToken cancellationToken = default);
}
