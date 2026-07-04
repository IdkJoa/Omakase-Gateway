using Application.Common.RiskEngine.AnomalyDetection;
using Infrastructure.AnomalyDetection;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas de la caché de modelos por usuario (HU-016/017): construye el modelo una sola vez y
/// <see cref="IAnomalyModelCache.Clear"/> fuerza la reconstrucción — base del reentrenamiento (T-035).
/// </summary>
public class AnomalyModelCacheTests
{
    private static AnomalyModel BuildModel()
    {
        var options = new AnomalyDetectionOptions { PcaRank = 3, MinTrainingSamples = 10 };
        var window = Enumerable.Range(0, 30)
            .Select(i => new AnomalyFeatureVector(
                0.40f + (i % 5) * 0.05f,
                0.30f + (i % 3) * 0.05f,
                0.10f + (i % 4) * 0.03f,
                0.20f + (i % 6) * 0.02f))
            .ToList();
        return new AnomalyModelTrainer(options, seed: 1).Train(window);
    }

    [Fact]
    public void GetOrBuild_BuildsOnce_ThenServesCached()
    {
        var cache = new AnomalyModelCache();
        var builds = 0;
        AnomalyModel Factory() { builds++; return BuildModel(); }

        cache.GetOrBuild("u", Factory);
        cache.GetOrBuild("u", Factory);

        Assert.Equal(1, builds);
    }

    [Fact]
    public void Clear_ForcesRebuild_OnNextGet()
    {
        var cache = new AnomalyModelCache();
        var builds = 0;
        AnomalyModel Factory() { builds++; return BuildModel(); }

        cache.GetOrBuild("u", Factory);
        cache.Clear();
        cache.GetOrBuild("u", Factory);

        Assert.Equal(2, builds);
    }
}
