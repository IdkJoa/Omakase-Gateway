using Application.Common.RiskEngine.AnomalyDetection;
using Infrastructure.AnomalyDetection;
using Xunit;

namespace OG.Application.UnitTests;

// Modelo de anomalías (HU-016 / T-032): el Anomaly Score calibrado por percentil separa normal de anómalo, a diferencia del score crudo del PCA.
public class AnomalyModelTests
{
    private const int Seed = 7;

    private static readonly AnomalyDetectionOptions Options = new() { PcaRank = 3, MinTrainingSamples = 20 };

    /// <summary>Construye un vector con la misma codificación cíclica que el FeatureExtractor.</summary>
    private static AnomalyFeatureVector Vec(double hour, float freq, float diversity)
    {
        var rad = 2.0 * Math.PI * hour / 24.0;
        return new AnomalyFeatureVector(
            (float)((Math.Sin(rad) + 1) / 2),
            (float)((Math.Cos(rad) + 1) / 2),
            freq,
            diversity);
    }

    /// <summary>Baseline creíble de "usuario de oficina": 9–17h, baja frecuencia y diversidad.</summary>
    private static List<AnomalyFeatureVector> OfficeBaseline(int count = 300)
    {
        var rng = new Random(Seed);
        var baseline = new List<AnomalyFeatureVector>(count);
        for (var i = 0; i < count; i++)
        {
            var hour = 9 + rng.NextDouble() * 8;                 // 09:00–17:00
            var freq = 0.05f + (float)rng.NextDouble() * 0.20f;   // baja
            var div = 0.10f + (float)rng.NextDouble() * 0.20f;    // baja
            baseline.Add(Vec(hour, freq, div));
        }
        return baseline;
    }

    private static AnomalyModel TrainedModel() =>
        new AnomalyModelTrainer(Options, Seed).Train(OfficeBaseline());

    [Fact]
    public void CalibratedScore_RanksAnomalyAboveNormal()
    {
        var model = TrainedModel();

        var normal = model.Score(Vec(13, 0.12f, 0.18f));    // dentro del patrón
        var anomaly = model.Score(Vec(3, 0.95f, 0.95f));     // ráfaga a las 03:00 AM

        // Esta es la propiedad que el score CRUDO de RandomizedPCA fallaba (spike).
        Assert.True(anomaly > normal, $"anomaly={anomaly:F1} debería superar normal={normal:F1}");
    }

    [Fact]
    public void CalibratedScore_FlagsVolumeBurst_PerAcceptanceCriteria()
    {
        var model = TrainedModel();

        // AC de HU-016: ráfaga de peticiones (alta frecuencia) para un usuario de baja frecuencia.
        var burst = model.Score(Vec(13, 0.95f, 0.20f));
        var normal = model.Score(Vec(13, 0.12f, 0.18f));

        Assert.True(burst > 60d, $"una ráfaga de volumen debería puntuar alto, fue {burst:F1}");
        Assert.True(burst > normal, $"burst={burst:F1} debería superar normal={normal:F1}");
    }

    [Fact]
    public void CalibratedScore_StaysWithinZeroToHundred()
    {
        var model = TrainedModel();

        foreach (var v in new[] { Vec(13, 0.1f, 0.1f), Vec(3, 0.99f, 0.99f), Vec(0, 0f, 0f), Vec(23, 1f, 1f) })
        {
            var score = model.Score(v);
            Assert.InRange(score, 0d, 100d);
        }
    }

    [Fact]
    public void TypicalBaselinePoint_DoesNotScoreAsExtremeAnomaly()
    {
        var model = TrainedModel();

        // Un punto en el corazón del baseline debe quedar en la mitad baja del rango.
        var score = model.Score(Vec(13, 0.15f, 0.20f));

        Assert.True(score < 60d, $"un patrón típico no debería puntuar {score:F1} (zona de anomalía)");
    }

    [Fact]
    public void Training_IsDeterministic_WithSameSeed()
    {
        var a = new AnomalyModelTrainer(Options, Seed).Train(OfficeBaseline());
        var b = new AnomalyModelTrainer(Options, Seed).Train(OfficeBaseline());

        var probe = Vec(3, 0.95f, 0.95f);
        Assert.Equal(a.Score(probe), b.Score(probe), 4);
    }

    [Fact]
    public void Train_Throws_WhenBaselineBelowColdStartThreshold()
    {
        var tooFew = OfficeBaseline(count: 5);
        var trainer = new AnomalyModelTrainer(Options, Seed);

        Assert.Throws<InvalidOperationException>(() => trainer.Train(tooFew));
    }
}
