namespace Application.Common.RiskEngine.AnomalyDetection;

/// <summary>
/// Configuración tipada del extractor de características (options pattern, sin hardcode).
/// Gobierna la ventana temporal y la saturación con que se normaliza la frecuencia (T-031).
/// </summary>
public sealed class AnomalyDetectionOptions
{
    public const string SectionName = "AnomalyDetection";

    /// <summary>Ventana, en minutos, sobre la que se miden frecuencia y diversidad. Default: 60.</summary>
    public int FrequencyWindowMinutes { get; set; } = 60;

    /// <summary>
    /// Número de peticiones en la ventana que mapea a frecuencia 1.0 (saturación).
    /// Por encima de este valor la frecuencia se satura en 1.0. Default: 60.
    /// </summary>
    public int FrequencySaturation { get; set; } = 60;

    /// <summary>
    /// Rango (número de componentes principales) del RandomizedPCA. Debe ser menor que
    /// <see cref="AnomalyFeatureVector.Dimension"/>. Con <c>rank = Dimension − 1 = 3</c> el residual
    /// recae en la dimensión de menor varianza del baseline (frecuencia/diversidad), que es justo
    /// donde viven las anomalías de volumen. Validado empíricamente como la mejor separación. Default: 3.
    /// </summary>
    public int PcaRank { get; set; } = 3;

    /// <summary>
    /// Mínimo de muestras de baseline requeridas para entrenar un modelo por usuario. Por debajo,
    /// el usuario sigue en cold-start (HU-017) y no hay modelo (el detector degrada a incertidumbre). Default: 20.
    /// </summary>
    public int MinTrainingSamples { get; set; } = 20;

    /// <summary>
    /// Mínimo de accesos DENTRO de la ventana de frecuencia para que las features de volumen se
    /// consideren medibles. Por debajo, el detector degrada a incertidumbre en vez de puntuar.
    /// <para>
    /// Motivo estadístico: <c>diversity = únicos / total</c> es un ratio sobre la muestra de la
    /// ventana. Con 1 acceso solo puede valer 1.0, con 2 solo 0.5 o 1.0 — una rejilla degenerada que
    /// no aparece en el baseline (donde ronda 0.2–0.3) y que empuja el percentil al extremo. Medido
    /// en vivo (2026-08-01): un único acceso previo bastaba para que un usuario legítimo puntuara
    /// 92.5, o sea que su SEGUNDA petición de la hora habría sido desafiada.
    /// </para>
    /// <para>
    /// Default 5: es el conteo más bajo con granularidad razonable, y coincide con el usuario normal
    /// que describe el SRS (~5 accesos/día). Un ataque por volumen (50 peticiones) lo supera de sobra,
    /// así que la detección no se pierde.
    /// </para>
    /// </summary>
    public int MinWindowSamples { get; set; } = 5;

    /// <summary>
    /// Vigencia de la caché Redis del perfil (<c>profile:{userId}</c>), en minutos, para no pegar a
    /// PostgreSQL en la ruta crítica (T-033). Default: 60 (1 hora, por especificación del SRS).
    /// </summary>
    public int ProfileCacheTtlMinutes { get; set; } = 60;

    /// <summary>Tope de vectores en la ventana de entrenamiento (rodante). Default: 200.</summary>
    public int TrainingWindowMax { get; set; } = 200;

    /// <summary>Tope de accesos recientes conservados para derivar frecuencia/diversidad. Default: 100.</summary>
    public int RecentAccessesMax { get; set; } = 100;

    /// <summary>Capacidad del canal de actualización de perfil (fire-and-forget). Default: 10000.</summary>
    public int ProfileUpdateChannelCapacity { get; set; } = 10_000;

    /// <summary>Periodo del reentrenamiento programado de los modelos por usuario, en horas (T-035). Default: 24.</summary>
    public int RetrainIntervalHours { get; set; } = 24;
}
