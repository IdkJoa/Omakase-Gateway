using Application.Common.RiskEngine.AnomalyDetection;
using Microsoft.ML;

namespace Infrastructure.AnomalyDetection;

/// <summary>
/// Modelo de anomalías entrenado para un usuario (HU-016 / T-032): envuelve el transformer
/// RandomizedPCA de ML.NET más su calibración, y expone un Anomaly Score honesto 0–100.
/// <para>
/// <b>Calibración:</b> el score crudo de RandomizedPCA es un ratio de error de reconstrucción que
/// no traquea linealmente "qué tan anómalo" es un punto (infla vectores cercanos al centroide). En su
/// lugar mapeamos el score crudo a su <b>percentil dentro de la distribución del baseline del usuario</b>:
/// un patrón típico cae en un percentil bajo (score bajo), uno inusual en un percentil alto (score alto).
/// Esto produce un 0–100 fiel y evita falsos positivos (RF-M3).
/// </para>
/// <para><b>Thread-safety:</b> el <see cref="PredictionEngine{TSrc,TDst}"/> no es reentrante; se protege
/// con un lock. Suficiente para un modelo por usuario; la inferencia es de microsegundos.</para>
/// </summary>
public sealed class AnomalyModel
{
    private readonly MLContext _ml;
    private readonly PredictionEngine<AnomalyFeatureRow, AnomalyRawPrediction> _engine;
    private readonly float[] _baselineScoresAscending;
    private readonly object _gate = new();

    internal AnomalyModel(MLContext ml, ITransformer transformer, float[] baselineScoresAscending)
    {
        _ml = ml;
        _engine = ml.Model.CreatePredictionEngine<AnomalyFeatureRow, AnomalyRawPrediction>(transformer);
        _baselineScoresAscending = baselineScoresAscending;
    }

    /// <summary>Número de muestras de baseline con que se calibró el modelo.</summary>
    public int BaselineSize => _baselineScoresAscending.Length;

    /// <summary>
    /// Puntúa una petición devolviendo el Anomaly Score calibrado en <c>[0,100]</c>.
    /// </summary>
    public double Score(AnomalyFeatureVector features)
    {
        var row = AnomalyFeatureRow.From(features);

        float raw;
        lock (_gate)
        {
            raw = _engine.Predict(row).Score;
        }

        return Calibrate(raw);
    }

    /// <summary>
    /// Mapea el score crudo a percentil dentro del baseline: <c>100 · P(baseline ≤ raw)</c>.
    /// Sin baseline (no debería ocurrir) devuelve 50 — máxima incertidumbre, alineado con el
    /// fallback de RF-M9.
    /// </summary>
    private double Calibrate(float raw)
    {
        var n = _baselineScoresAscending.Length;
        if (n == 0)
            return 50d;

        // Cota superior: primer índice cuyo valor es > raw ⇒ cuántos baseline son ≤ raw.
        var lo = 0;
        var hi = n;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (_baselineScoresAscending[mid] <= raw)
                lo = mid + 1;
            else
                hi = mid;
        }

        return 100d * lo / n;
    }
}
