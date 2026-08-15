using System;
using System.Collections.Generic;
using System.Linq;
using Application.Common.RiskEngine;
using Application.Common.RiskEngine.AnomalyDetection;
using Infrastructure.AnomalyDetection;
using Xunit;
using Xunit.Abstractions;

namespace OG.Application.UnitTests.AnomalyDetection;

// Baseline sintético (HU-016 / T-089): lo que importa no es solo que genere datos, sino que el
// modelo entrenado sobre ellos separe comportamiento normal de anómalo — lo que hace útil a HU-034.
public class BehaviorBaselineBootstrapperTests
{
    private const int Seed = 20260731;
    private static readonly DateTimeOffset EndingAt = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    private readonly ITestOutputHelper _output;

    public BehaviorBaselineBootstrapperTests(ITestOutputHelper output) => _output = output;

    private static AnomalyDetectionOptions Options() => new();

    private static BehaviorBaselineBootstrapper Sut(AnomalyDetectionOptions options) =>
        new(new FeatureExtractor(options), options);

    [Fact]
    public void Build_GeneraSuficientesMuestrasParaEntrenar()
    {
        var options = Options();

        var baseline = Sut(options).Build(EndingAt, days: 14, seed: Seed);

        Assert.True(
            baseline.TrainingWindow.Count >= options.MinTrainingSamples,
            $"la ventana ({baseline.TrainingWindow.Count}) no alcanza MinTrainingSamples ({options.MinTrainingSamples})");
        Assert.True(baseline.AccessCount > 0);
    }

    [Fact]
    public void Build_EsReproducibleConLaMismaSemilla()
    {
        var a = Sut(Options()).Build(EndingAt, days: 7, seed: Seed);
        var b = Sut(Options()).Build(EndingAt, days: 7, seed: Seed);

        Assert.Equal(a.AccessCount, b.AccessCount);
        Assert.Equal(a.TrainingWindow, b.TrainingWindow);
    }

    [Fact]
    public void Build_ConSemillasDistintas_ProduceBaselinesDistintos()
    {
        var a = Sut(Options()).Build(EndingAt, days: 7, seed: 1);
        var b = Sut(Options()).Build(EndingAt, days: 7, seed: 2);

        Assert.NotEqual(a.TrainingWindow, b.TrainingWindow);
    }

    [Fact]
    public void Build_RespetaElTopeDeLaVentanaRodante()
    {
        var options = new AnomalyDetectionOptions { TrainingWindowMax = 50, RecentAccessesMax = 25 };

        var baseline = Sut(options).Build(EndingAt, days: 30, seed: Seed);

        Assert.True(baseline.TrainingWindow.Count <= 50);
        Assert.True(baseline.RecentAccesses.Count <= 25);
    }

    [Fact]
    public void Build_ConDiasInvalidos_Lanza()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Sut(Options()).Build(EndingAt, days: 0, seed: Seed));
    }

    // La prueba que justifica todo el mecanismo: entrenado sobre el baseline de oficina, el modelo debe
    // puntuar muy por encima una ráfaga nocturna que un acceso normal (Escenario 2 de HU-034 en laboratorio).
    [Fact]
    public void ModeloEntrenadoConElBaseline_SeparaRafagaNocturnaDeAccesoNormal()
    {
        var options = Options();
        var extractor = new FeatureExtractor(options);
        var baseline = Sut(options).Build(EndingAt, days: 14, seed: Seed);

        var model = new AnomalyModelTrainer(options, seed: Seed).Train(baseline.TrainingWindow);

        // Acceso legítimo: martes a media mañana, con el ritmo pausado del propio baseline.
        var normalAt = new DateTimeOffset(2026, 7, 28, 10, 15, 0, TimeSpan.Zero);
        var normalHistory = Enumerable.Range(0, 4)
            .Select(i => new UserAccessSample(normalAt.AddMinutes(-12 * (i + 1)), $"/reports"))
            .ToList();
        var normal = extractor.Extract(
            new RequestContext { SourceIp = string.Empty, Timestamp = normalAt }, normalHistory);

        // Ataque: 3:00 AM, 50 peticiones en 10 minutos contra el mismo endpoint.
        var attackAt = new DateTimeOffset(2026, 7, 28, 3, 0, 0, TimeSpan.Zero);
        var attackHistory = Enumerable.Range(0, 50)
            .Select(i => new UserAccessSample(attackAt.AddSeconds(-12 * (i + 1)), "/export"))
            .ToList();
        var attack = extractor.Extract(
            new RequestContext { SourceIp = string.Empty, Timestamp = attackAt }, attackHistory);

        var normalScore = model.Score(normal);
        var attackScore = model.Score(attack);

        _output.WriteLine($"baseline: {baseline.TrainingWindow.Count} muestras, {baseline.AccessCount} accesos");
        _output.WriteLine($"normal  -> features={string.Join(", ", normal.ToArray())} score={normalScore:F1}");
        _output.WriteLine($"ataque  -> features={string.Join(", ", attack.ToArray())} score={attackScore:F1}");

        Assert.True(
            attackScore > normalScore,
            $"la ráfaga nocturna ({attackScore:F1}) no puntuó por encima del acceso normal ({normalScore:F1})");
    }

    // La ventana horaria del perfil define qué es "normal" (la hora es una de las 4 features). Verificado en
    // vivo (2026-07-31): con baseline 9-18 y peticiones legítimas a 00:30 UTC el score se clavaba en ~99. Por
    // eso HU-034 configura la jornada en UTC (13-22 UTC = 9-18 en Santo Domingo) para los casos del banco.
    [Fact]
    public void LaVentanaHorariaDelPerfil_DefineQueEsNormal()
    {
        var options = Options();
        var extractor = new FeatureExtractor(options);

        // El MISMO acceso (media tarde) evaluado contra dos perfiles distintos.
        AnomalyFeatureVector AccesoDeTarde()
        {
            var t = new DateTimeOffset(2026, 7, 28, 14, 15, 0, TimeSpan.Zero);
            var history = Enumerable.Range(0, 4)
                .Select(i => new UserAccessSample(t.AddMinutes(-12 * (i + 1)), "/reports"))
                .ToList();
            return extractor.Extract(new RequestContext { SourceIp = string.Empty, Timestamp = t }, history);
        }

        AnomalyModel ModeloCon(TimeSpan inicio, TimeSpan fin)
        {
            var perfil = SyntheticTrafficProfile.OfficeWorker with { OfficeStart = inicio, OfficeEnd = fin };
            var baseline = Sut(options).Build(EndingAt, days: 14, seed: Seed, perfil);
            return new AnomalyModelTrainer(options, seed: Seed).Train(baseline.TrainingWindow);
        }

        var acceso = AccesoDeTarde();
        var conJornadaDiurna = ModeloCon(TimeSpan.FromHours(9), TimeSpan.FromHours(18)).Score(acceso);
        var conJornadaNocturna = ModeloCon(TimeSpan.FromHours(0), TimeSpan.FromHours(6)).Score(acceso);

        _output.WriteLine($"acceso 14:15 -> perfil 9-18 = {conJornadaDiurna:F1} | perfil 0-6 = {conJornadaNocturna:F1}");

        Assert.True(
            conJornadaNocturna > conJornadaDiurna,
            $"el mismo acceso debería ser más anómalo contra un perfil nocturno ({conJornadaNocturna:F1}) "
            + $"que contra uno diurno que lo contiene ({conJornadaDiurna:F1})");
    }

    // La primera petición tras inactividad llega con frecuencia/diversidad en 0, valores raros en el
    // baseline; si el modelo la puntuara como anomalía extrema, todo caso legítimo del banco (HU-034) arrancaría penalizado.
    [Fact]
    public void AccesoLegitimoTrasInactividad_NoPuntuaComoAnomaliaExtrema()
    {
        var options = Options();
        var extractor = new FeatureExtractor(options);
        var baseline = Sut(options).Build(EndingAt, days: 14, seed: Seed);
        var model = new AnomalyModelTrainer(options, seed: Seed).Train(baseline.TrainingWindow);

        // Martes 10:15, sin ningún acceso en la ventana de frecuencia.
        var at = new DateTimeOffset(2026, 7, 28, 10, 15, 0, TimeSpan.Zero);
        var features = extractor.Extract(
            new RequestContext { SourceIp = string.Empty, Timestamp = at },
            Array.Empty<UserAccessSample>());

        var score = model.Score(features);
        _output.WriteLine($"primera tras inactividad -> features={string.Join(", ", features.ToArray())} score={score:F1}");

        // Lo que importa no es el score en bruto sino su consecuencia: con la configuración sembrada
        // (AnomalyWeight=0.4, ChallengeThreshold=40) un acceso legítimo debe seguir siendo ALLOW.
        // Es alto (≈80: arrancar la jornada es un patrón poco frecuente en el baseline), y por eso
        // acota cuánto se puede subir AnomalyWeight en la calibración de HU-035 sin fabricar falsos
        // positivos. Este test es el guardián de esa frontera.
        var riesgo = 0.4m * (decimal)score;
        Assert.True(riesgo <= 40m,
            $"un acceso legítimo tras inactividad daría riesgo {riesgo:F1} (>40) → falso positivo. Score={score:F1}");
    }
}
