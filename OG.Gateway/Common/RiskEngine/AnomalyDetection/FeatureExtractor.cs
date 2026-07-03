namespace Application.Common.RiskEngine.AnomalyDetection;

/// <summary>
/// Implementación pura del <see cref="IFeatureExtractor"/> (T-031). Sin estado y sin I/O:
/// toda la data que necesita llega por parámetro, de modo que es determinista y unit-testeable.
/// Registrable como Singleton.
/// </summary>
public sealed class FeatureExtractor : IFeatureExtractor
{
    private readonly AnomalyDetectionOptions _options;

    public FeatureExtractor(AnomalyDetectionOptions options) => _options = options;

    /// <inheritdoc/>
    public AnomalyFeatureVector Extract(RequestContext context, IReadOnlyList<UserAccessSample> recentAccesses)
    {
        var (hourSin, hourCos) = EncodeHour(context.Timestamp);
        var (frequency, diversity) = MeasureWindow(context.Timestamp, recentAccesses);

        return new AnomalyFeatureVector(hourSin, hourCos, frequency, diversity);
    }

    /// <summary>
    /// Codifica la hora del día como <c>sin(2π·h/24)</c> y <c>cos(2π·h/24)</c>, reescalados a
    /// <c>[0,1]</c>. La codificación cíclica hace que 23:00 y 00:00 queden adyacentes (RF-M3).
    /// </summary>
    private static (float sin, float cos) EncodeHour(DateTimeOffset timestamp)
    {
        var hours = timestamp.TimeOfDay.TotalHours;      // hora fraccionaria [0,24)
        var radians = 2.0 * Math.PI * hours / 24.0;

        var sin = (float)((Math.Sin(radians) + 1.0) / 2.0);
        var cos = (float)((Math.Cos(radians) + 1.0) / 2.0);
        return (sin, cos);
    }

    /// <summary>
    /// Calcula frecuencia y diversidad sobre los accesos dentro de la ventana
    /// <c>(now − FrequencyWindow, now]</c>. Frecuencia = conteo / saturación (clamp a 1.0);
    /// diversidad = endpoints únicos / total (0 si no hay historial en la ventana).
    /// </summary>
    private (float frequency, float diversity) MeasureWindow(
        DateTimeOffset now, IReadOnlyList<UserAccessSample> recentAccesses)
    {
        if (recentAccesses is null || recentAccesses.Count == 0)
            return (0f, 0f);

        var windowStart = now - TimeSpan.FromMinutes(_options.FrequencyWindowMinutes);

        var count = 0;
        var unique = new HashSet<string>(StringComparer.Ordinal);

        foreach (var access in recentAccesses)
        {
            if (access.Timestamp <= windowStart || access.Timestamp > now)
                continue;

            count++;
            unique.Add(access.Endpoint ?? string.Empty);
        }

        if (count == 0)
            return (0f, 0f);

        var frequency = _options.FrequencySaturation <= 0
            ? 0f
            : Math.Min(count / (float)_options.FrequencySaturation, 1f);

        var diversity = unique.Count / (float)count;   // en (0,1]

        return (frequency, diversity);
    }
}
