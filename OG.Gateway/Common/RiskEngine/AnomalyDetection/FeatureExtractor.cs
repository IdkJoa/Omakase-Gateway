namespace Application.Common.RiskEngine.AnomalyDetection;

public sealed class FeatureExtractor : IFeatureExtractor
{
    private readonly AnomalyDetectionOptions _options;

    public FeatureExtractor(AnomalyDetectionOptions options) => _options = options;

    public AnomalyFeatureVector Extract(RequestContext context, IReadOnlyList<UserAccessSample> recentAccesses)
    {
        var (hourSin, hourCos) = EncodeHour(context.Timestamp);
        var (frequency, diversity) = MeasureWindow(context.Timestamp, recentAccesses);

        return new AnomalyFeatureVector(hourSin, hourCos, frequency, diversity);
    }

    // Codificación cíclica sin/cos reescalada a [0,1] para que 23:00 y 00:00 queden adyacentes.
    private static (float sin, float cos) EncodeHour(DateTimeOffset timestamp)
    {
        var hours = timestamp.TimeOfDay.TotalHours;
        var radians = 2.0 * Math.PI * hours / 24.0;

        var sin = (float)((Math.Sin(radians) + 1.0) / 2.0);
        var cos = (float)((Math.Cos(radians) + 1.0) / 2.0);
        return (sin, cos);
    }

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

        var diversity = unique.Count / (float)count;

        return (frequency, diversity);
    }
}
