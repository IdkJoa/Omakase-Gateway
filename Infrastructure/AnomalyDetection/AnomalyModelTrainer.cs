using Application.Common.RiskEngine.AnomalyDetection;
using Microsoft.ML;
using Microsoft.ML.Trainers;

namespace Infrastructure.AnomalyDetection;

/// <summary>
/// Entrena el modelo de anomalías por usuario con <c>RandomizedPcaTrainer</c> nativo de ML.NET
/// (HU-016 / T-032). Aprende el subespacio principal del comportamiento del baseline y calcula
/// la calibración (distribución de scores del baseline) que <see cref="AnomalyModel"/> usa para
/// producir un Anomaly Score 0–100 fiel.
/// <para>Sin estado (una instancia por proceso) → registrable como Singleton.</para>
/// </summary>
public sealed class AnomalyModelTrainer
{
    private readonly AnomalyDetectionOptions _options;
    private readonly int? _seed;

    public AnomalyModelTrainer(AnomalyDetectionOptions options, int? seed = null)
    {
        _options = options;
        _seed = seed;
    }

    /// <exception cref="InvalidOperationException">
    /// Si el baseline tiene menos de <see cref="AnomalyDetectionOptions.MinTrainingSamples"/> muestras:
    /// el usuario sigue en cold-start (HU-017) y no debe entrenarse un modelo poco fiable.
    /// </exception>
    public AnomalyModel Train(IReadOnlyCollection<AnomalyFeatureVector> baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        if (baseline.Count < _options.MinTrainingSamples)
            throw new InvalidOperationException(
                $"Baseline insuficiente para entrenar: {baseline.Count} < {_options.MinTrainingSamples} (cold-start).");

        var ml = new MLContext(seed: _seed);
        var data = ml.Data.LoadFromEnumerable(baseline.Select(AnomalyFeatureRow.From));

        // Las features ya vienen normalizadas a [0,1] del FeatureExtractor (T-031): añadir MinMax/z-score
        // aquí distorsiona esa escala natural y degrada la separación (verificado empíricamente). Por eso
        // se entrena directo sobre las features crudas.
        //
        // EnsureZeroMean=false a propósito: centrar en la media hace que RandomizedPCA normalice a norma
        // unitaria y penalice los puntos CERCANOS al centroide (los más normales) — artefacto verificado.
        // Sin centrar, la anomalía se mide por DIRECCIÓN: un patrón fuera del subespacio del baseline
        // (frecuencia/diversidad altas) queda ortogonal al subespacio y puntúa alto.
        var trainer = ml.AnomalyDetection.Trainers.RandomizedPca(new RandomizedPcaTrainer.Options
        {
            FeatureColumnName = "Features",
            Rank = _options.PcaRank,
            EnsureZeroMean = false,
            Seed = _seed,
        });

        var transformer = trainer.Fit(data);

        var baselineScores = ScoreBaseline(ml, transformer, data);

        return new AnomalyModel(ml, transformer, baselineScores);
    }

    // Devuelve los scores crudos ordenados ascendentemente: insumo de la calibración por percentil.
    private static float[] ScoreBaseline(MLContext ml, ITransformer transformer, IDataView data)
    {
        var scored = transformer.Transform(data);
        var scores = ml.Data
            .CreateEnumerable<AnomalyRawPrediction>(scored, reuseRowObject: false)
            .Select(p => p.Score)
            .ToArray();

        Array.Sort(scores);
        return scores;
    }
}
