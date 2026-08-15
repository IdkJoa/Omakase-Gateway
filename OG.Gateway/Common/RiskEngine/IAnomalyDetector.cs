namespace Application.Common.RiskEngine;

// Seam para que el handler no dependa del modelo real: cambiar la implementación registrada en DI
// no requiere tocar el handler.
public interface IAnomalyDetector
{
    Task<decimal> GetAnomalyScoreAsync(RequestContext context, CancellationToken cancellationToken = default);
}

// Devuelve 50 (máxima incertidumbre) — el mismo valor que el fallback de RF-M9 cuando ML.NET falla,
// así que no es código desechable: es la ruta de degradación que igual habría que escribir.
public sealed class StubAnomalyDetector : IAnomalyDetector
{
    public Task<decimal> GetAnomalyScoreAsync(RequestContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(50m);
}
