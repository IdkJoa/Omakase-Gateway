using System.Diagnostics.Metrics;

namespace Infrastructure.Resilience;

/// <summary>
/// Métricas de OpenTelemetry para registrar apertura/cierre/half-open de Circuit Breakers (T-067 / HU-031).
/// </summary>
public static class ResilienceMetrics
{
    public const string MeterName = "Omakase.Resilience";

    private static readonly Meter ResilienceMeter = new(MeterName, "1.0.0");

    // Contador total de cambios de estado del Circuit Breaker
    private static readonly Counter<long> StateChangeCounter = ResilienceMeter.CreateCounter<long>(
        "omakase_circuit_breaker_state_changes_total",
        "count",
        "Número total de cambios de estado en los Circuit Breakers de dependencias");

    // Gauge para monitorear el estado actual del circuito (0 = Closed, 1 = HalfOpen, 2 = Open)
    private static readonly UpDownCounter<long> RedisCircuitStateGauge = ResilienceMeter.CreateUpDownCounter<long>(
        "omakase_circuit_breaker_redis_state",
        "state",
        "Estado del Circuit Breaker de Redis (0=Closed, 1=HalfOpen, 2=Open)");

    private static readonly UpDownCounter<long> PostgresCircuitStateGauge = ResilienceMeter.CreateUpDownCounter<long>(
        "omakase_circuit_breaker_postgres_state",
        "state",
        "Estado del Circuit Breaker de PostgreSQL (0=Closed, 1=HalfOpen, 2=Open)");

    public static void RecordStateChange(string dependency, string newState)
    {
        StateChangeCounter.Add(1, 
            new KeyValuePair<string, object?>("dependency", dependency),
            new KeyValuePair<string, object?>("new_state", newState));

        long numericState = newState.ToLowerInvariant() switch
        {
            "closed" => 0,
            "halfopen" or "half_open" => 1,
            "open" => 2,
            _ => -1
        };

        if (dependency.Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            RedisCircuitStateGauge.Add(numericState);
        }
        else if (dependency.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            PostgresCircuitStateGauge.Add(numericState);
        }
    }
}
