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
