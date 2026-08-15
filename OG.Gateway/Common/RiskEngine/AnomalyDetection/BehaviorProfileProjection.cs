namespace Application.Common.RiskEngine.AnomalyDetection;

public readonly record struct BehaviorProfileSummary(
    float HourSin,
    float HourCos,
    float Frequency,
    float Diversity,
    double TypicalHour);

// Función pura (sin estado ni dependencias) para que el Dashboard (T-054) la reutilice y sea testeable.
public static class BehaviorProfileProjection
{
    // Null si la ventana está vacía (cold-start), para que el consumidor lo distinga de un vector en cero.
    // La hora se decodifica con media circular (Atan2 sobre seno/coseno) en vez de promedio aritmético,
    // porque promediar directo rompería la continuidad cíclica (23:00 y 01:00 promediarían a 12:00).
    public static BehaviorProfileSummary? Summarize(IReadOnlyList<AnomalyFeatureVector> window)
    {
        if (window is null || window.Count == 0)
            return null;

        double sumSin = 0, sumCos = 0, sumFreq = 0, sumDiv = 0;
        foreach (var v in window)
        {
            sumSin += v.HourSin;
            sumCos += v.HourCos;
            sumFreq += v.Frequency;
            sumDiv += v.Diversity;
        }

        var n = window.Count;
        var avgSin = sumSin / n;
        var avgCos = sumCos / n;

        var angle = Math.Atan2(2.0 * avgSin - 1.0, 2.0 * avgCos - 1.0);
        if (angle < 0) angle += 2.0 * Math.PI;
        var typicalHour = angle / (2.0 * Math.PI) * 24.0;

        return new BehaviorProfileSummary(
            (float)avgSin, (float)avgCos, (float)(sumFreq / n), (float)(sumDiv / n), typicalHour);
    }
}
