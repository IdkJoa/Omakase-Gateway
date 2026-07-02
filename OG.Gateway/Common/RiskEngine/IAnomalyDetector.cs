namespace Application.Common.RiskEngine;

/// <summary>
/// Produce el Anomaly Score (0–100) de la capa de IA para una petición.
/// Costura (seam) para que HU-015 no dependa del modelo real: en Sprint 3 se
/// cambia el registro de DI por la implementación ML.NET, sin tocar el handler.
/// </summary>
public interface IAnomalyDetector
{
    Task<decimal> GetAnomalyScoreAsync(RequestContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Stub de Sprint 2: devuelve 50 (máxima incertidumbre), el mismo valor que el
/// fallback de RF-M9 cuando ML.NET falla y el del ejemplo de T-029. No es código
/// desechable: es la ruta de degradación que igual habría que escribir.
/// </summary>
public sealed class StubAnomalyDetector : IAnomalyDetector
{
    public Task<decimal> GetAnomalyScoreAsync(RequestContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(50m);
}
