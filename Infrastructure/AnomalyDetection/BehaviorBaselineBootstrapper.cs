using Application.Common.RiskEngine;
using Application.Common.RiskEngine.AnomalyDetection;

namespace Infrastructure.AnomalyDetection;

/// <summary>Baseline de comportamiento ya construido, listo para persistir en <c>user_behavior_profiles</c>.</summary>
public sealed record BehaviorBaseline(
    IReadOnlyList<AnomalyFeatureVector> TrainingWindow,
    IReadOnlyList<UserAccessSample> RecentAccesses,
    int AccessCount);

/// <summary>
/// Construye un baseline de comportamiento realista a partir del <see cref="SyntheticTrafficGenerator"/>
/// (HU-016 / T-089), para que el modelo de anomalías tenga algo defendible que aprender sin esperar
/// semanas de tráfico real.
/// <para>
/// <b>Por qué existe.</b> <see cref="RandomizedPcaAnomalyDetector"/> exige
/// <see cref="AnomalyDetectionOptions.MinTrainingSamples"/> muestras antes de entrenar; por debajo
/// devuelve 50 neutro. Conseguirlas a base de una ráfaga de peticiones en dos minutos produce un
/// baseline degenerado —todas a la misma hora, frecuencia creciente, diversidad decreciente— que le
/// enseña al modelo que "muchas peticiones seguidas" es lo normal, justo lo contrario de lo que debe
/// detectar. Es el "síndrome de datos robóticos" que el generador se escribió para evitar.
/// </para>
/// <para>
/// <b>Honestidad metodológica.</b> El baseline es sintético y debe declararse como tal: modela una
/// jornada de oficina (doble pico, pausa de almuerzo, caída de fin de semana, jitter gaussiano y
/// endpoints con sesgo Zipf). Es reproducible con semilla, de modo que un tercero puede repetir el
/// experimento y obtener el mismo perfil. No sustituye tráfico real en producción: es el control
/// del experimento de HU-034.
/// </para>
/// <para>Puro y sin E/S: la persistencia es responsabilidad del llamador.</para>
/// </summary>
public sealed class BehaviorBaselineBootstrapper
{
    private readonly IFeatureExtractor _extractor;
    private readonly AnomalyDetectionOptions _options;

    public BehaviorBaselineBootstrapper(IFeatureExtractor extractor, AnomalyDetectionOptions options)
    {
        _extractor = extractor;
        _options = options;
    }

    /// <summary>
    /// Genera <paramref name="days"/> días de actividad que terminan en <paramref name="endingAt"/> y
    /// los pliega en un baseline, replicando exactamente el orden en que lo haría
    /// <see cref="ProfileUpdateWorker"/> en caliente: la feature de cada acceso se deriva del historial
    /// PREVIO, nunca de sí misma.
    /// </summary>
    public BehaviorBaseline Build(
        DateTimeOffset endingAt,
        int days,
        int seed,
        SyntheticTrafficProfile? profile = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);

        var spec = profile ?? SyntheticTrafficProfile.OfficeWorker;
        var samples = new SyntheticTrafficGenerator(seed)
            .Generate(endingAt.AddDays(-days), days, spec);

        var window = new List<AnomalyFeatureVector>();
        var recent = new List<UserAccessSample>();

        foreach (var sample in samples)
        {
            // Mismo contrato que en caliente: SourceIp vacío (el extractor no lo usa) y el historial
            // acumulado hasta ANTES de este acceso.
            var features = _extractor.Extract(
                new RequestContext { SourceIp = string.Empty, Timestamp = sample.Timestamp },
                recent);

            Append(window, features, _options.TrainingWindowMax);
            Append(recent, sample, _options.RecentAccessesMax);
        }

        return new BehaviorBaseline(window, recent, samples.Count);
    }

    // Ventana rodante: conserva a lo sumo max elementos, los más recientes.
    private static void Append<T>(List<T> target, T item, int max)
    {
        target.Add(item);
        if (target.Count > max)
            target.RemoveRange(0, target.Count - max);
    }
}
