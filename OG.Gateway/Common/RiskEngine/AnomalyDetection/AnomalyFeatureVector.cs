namespace Application.Common.RiskEngine.AnomalyDetection;

/// <summary>
/// Vector de características reducido y justificable que resume una petición para el
/// modelo de detección de anomalías (HU-016 / T-031 / RF-M3).
/// <para>
/// Las cuatro componentes están normalizadas a <c>[0,1]</c>:
/// <list type="bullet">
///   <item><see cref="HourSin"/>/<see cref="HourCos"/>: hora del día codificada con
///   seno y coseno, de modo que las 23:00 y las 00:00 quedan próximas (continuidad cíclica).</item>
///   <item><see cref="Frequency"/>: frecuencia de peticiones del usuario en la ventana reciente.</item>
///   <item><see cref="Diversity"/>: diversidad de endpoints (únicos / total) en la ventana.</item>
/// </list>
/// </para>
/// </summary>
public readonly record struct AnomalyFeatureVector(
    float HourSin,
    float HourCos,
    float Frequency,
    float Diversity)
{
    /// <summary>Número de componentes del vector (dimensión de entrada del modelo ML.NET).</summary>
    public const int Dimension = 4;

    /// <summary>Proyecta el vector al arreglo de <c>float</c> que consume el pipeline de ML.NET.</summary>
    public float[] ToArray() => new[] { HourSin, HourCos, Frequency, Diversity };
}
