namespace Application.Common.RiskEngine.AnomalyDetection;

// Las 4 componentes están normalizadas a [0,1]. HourSin/HourCos codifican la hora del día con
// seno/coseno para que 23:00 y 00:00 queden próximas (continuidad cíclica).
public readonly record struct AnomalyFeatureVector(
    float HourSin,
    float HourCos,
    float Frequency,
    float Diversity)
{
    public const int Dimension = 4;

    public float[] ToArray() => new[] { HourSin, HourCos, Frequency, Diversity };
}
