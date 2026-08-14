using Polly.CircuitBreaker;

namespace Infrastructure.Resilience;

public interface IDependencyCircuitBreaker
{
    bool IsRedisCircuitOpen { get; }
    bool IsPostgresCircuitOpen { get; }

    CircuitState RedisCircuitState { get; }
    CircuitState PostgresCircuitState { get; }

    Task<T> ExecuteRedisAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default);
    Task ExecuteRedisAsync(Func<Task> action, CancellationToken cancellationToken = default);

    Task<T> ExecutePostgresAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default);
    Task ExecutePostgresAsync(Func<Task> action, CancellationToken cancellationToken = default);
}
