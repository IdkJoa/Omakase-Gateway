namespace Application.Common.RiskEngine.AnomalyDetection;

public sealed class AnomalyDetectionOptions
{
    public const string SectionName = "AnomalyDetection";

    public int FrequencyWindowMinutes { get; set; } = 60;

    public int FrequencySaturation { get; set; } = 60;

    // rank = Dimension - 1 = 3: el residual recae en la dimensión de menor varianza del baseline
    // (frecuencia/diversidad), donde viven las anomalías de volumen. Validado empíricamente.
    public int PcaRank { get; set; } = 3;

    // Por debajo, el usuario sigue en cold-start (HU-017) y el detector degrada a incertidumbre.
    public int MinTrainingSamples { get; set; } = 20;

    // Con pocos accesos, diversity = únicos/total es una rejilla degenerada (1.0 con 1 acceso, 0.5/1.0
    // con 2) que no aparece en el baseline (0.2-0.3) y dispara el percentil al extremo: en vivo
    // (2026-08-01) un único acceso previo bastaba para puntuar 92.5 en la segunda petición de la hora.
    // Default 5 = mínimo con granularidad razonable, alineado al usuario normal del SRS (~5 accesos/día).
    public int MinWindowSamples { get; set; } = 5;

    public int ProfileCacheTtlMinutes { get; set; } = 60;

    public int TrainingWindowMax { get; set; } = 200;

    public int RecentAccessesMax { get; set; } = 100;

    public int ProfileUpdateChannelCapacity { get; set; } = 10_000;

    public int RetrainIntervalHours { get; set; } = 24;
}
