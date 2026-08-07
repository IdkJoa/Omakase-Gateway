using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Polly;
using Polly.CircuitBreaker;
using StackExchange.Redis;

namespace Infrastructure.Resilience;

/// <summary>
/// Implementación de Circuit Breaker para Redis y PostgreSQL usando Polly v8 (T-067 / HU-031).
/// </summary>
public sealed class DependencyCircuitBreaker : IDependencyCircuitBreaker
{
    private readonly ResiliencePipeline _redisPipeline;
    private readonly ResiliencePipeline _postgresPipeline;
    private readonly ILogger<DependencyCircuitBreaker> _logger;

    private volatile CircuitState _redisState = CircuitState.Closed;
    private volatile CircuitState _postgresState = CircuitState.Closed;

    public bool IsRedisCircuitOpen => RedisCircuitState == CircuitState.Open;
    public bool IsPostgresCircuitOpen => PostgresCircuitState == CircuitState.Open;

    public CircuitState RedisCircuitState => _redisState;
    public CircuitState PostgresCircuitState => _postgresState;

    public DependencyCircuitBreaker(ILogger<DependencyCircuitBreaker> logger)
    {
        _logger = logger;

        _redisPipeline = BuildPipeline(
            dependencyName: "Redis",
            shouldHandle: new PredicateBuilder()
                .Handle<RedisException>()
                .Handle<RedisConnectionException>()
                .Handle<RedisTimeoutException>()
                .Handle<System.Net.Sockets.SocketException>()
                .Handle<TimeoutException>(),
            onStateChanged: state => _redisState = state);

        _postgresPipeline = BuildPipeline(
            dependencyName: "PostgreSQL",
            shouldHandle: new PredicateBuilder()
                .Handle<NpgsqlException>()
                .Handle<DbUpdateException>()
                .Handle<DbException>()
                .Handle<System.Net.Sockets.SocketException>()
                .Handle<TimeoutException>(),
            onStateChanged: state => _postgresState = state);
    }

    private ResiliencePipeline BuildPipeline(
        string dependencyName, 
        PredicateBuilder<object> shouldHandle, 
        Action<CircuitState> onStateChanged)
    {
        return new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 1.0,               // 100% de fallos en el período de muestreo (3 fallos consecutivos)
                MinimumThroughput = 3,            // Mínimo 3 peticiones fallidas consecutivas para abrir
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(30), // Recuperación tras 30 segundos (Half-Open)
                ShouldHandle = shouldHandle,
                OnOpened = args =>
                {
                    onStateChanged(CircuitState.Open);
                    _logger.LogError(
                        "[CircuitBreaker] CIRCUITO ABIERTO para {Dependency}. Fallos consecutivos alcanzados. Breaker activo por 30s. Excepción: {Message}",
                        dependencyName, args.Outcome.Exception?.Message);

                    ResilienceMetrics.RecordStateChange(dependencyName, "Open");
                    Activity.Current?.AddEvent(new ActivityEvent($"CircuitBreaker_{dependencyName}_Opened"));
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    onStateChanged(CircuitState.Closed);
                    _logger.LogInformation(
                        "[CircuitBreaker] CIRCUITO CERRADO para {Dependency}. Servicio restablecido correctamente.",
                        dependencyName);

                    ResilienceMetrics.RecordStateChange(dependencyName, "Closed");
                    Activity.Current?.AddEvent(new ActivityEvent($"CircuitBreaker_{dependencyName}_Closed"));
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    onStateChanged(CircuitState.HalfOpen);
                    _logger.LogWarning(
                        "[CircuitBreaker] CIRCUITO HALF-OPEN para {Dependency}. Probando reconexión tras 30s.",
                        dependencyName);

                    ResilienceMetrics.RecordStateChange(dependencyName, "HalfOpen");
                    Activity.Current?.AddEvent(new ActivityEvent($"CircuitBreaker_{dependencyName}_HalfOpened"));
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public async Task<T> ExecuteRedisAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        return await _redisPipeline.ExecuteAsync(async _ => await action(), cancellationToken);
    }

    public async Task ExecuteRedisAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        await _redisPipeline.ExecuteAsync(async _ => await action(), cancellationToken);
    }

    public async Task<T> ExecutePostgresAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        return await _postgresPipeline.ExecuteAsync(async _ => await action(), cancellationToken);
    }

    public async Task ExecutePostgresAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        await _postgresPipeline.ExecuteAsync(async _ => await action(), cancellationToken);
    }
}
