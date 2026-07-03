using Application.Common.RiskEngine.AnomalyDetection;

namespace Infrastructure.AnomalyDetection;

/// <summary>
/// Perfil de comportamiento sintético de un usuario "de oficina" (HU-016 / T-089).
/// </summary>
/// <param name="OfficeStart">Inicio de la jornada.</param>
/// <param name="OfficeEnd">Fin de la jornada.</param>
/// <param name="MeanRequestsPerWorkday">Media de peticiones en un día laboral.</param>
/// <param name="WeekendActivityFactor">Fracción (0–1) de la actividad laboral que ocurre en fin de semana.</param>
/// <param name="Endpoints">Endpoints posibles (los primeros se visitan más → diversidad baja y realista).</param>
public sealed record SyntheticTrafficProfile(
    TimeSpan OfficeStart,
    TimeSpan OfficeEnd,
    int MeanRequestsPerWorkday,
    double WeekendActivityFactor,
    IReadOnlyList<string> Endpoints)
{
    /// <summary>Perfil por defecto: 9–18h, ~40 req/día, poca actividad de fin de semana, 5 endpoints.</summary>
    public static SyntheticTrafficProfile OfficeWorker { get; } = new(
        OfficeStart: TimeSpan.FromHours(9),
        OfficeEnd: TimeSpan.FromHours(18),
        MeanRequestsPerWorkday: 40,
        WeekendActivityFactor: 0.08,
        Endpoints: new[] { "/reports", "/dashboard", "/profile", "/search", "/export" });
}

/// <summary>
/// Generador de tráfico sintético probabilístico (HU-016 / T-089). Modela jornada de oficina con doble
/// pico (mañana/tarde) y pausa de almuerzo, menor actividad en fin de semana y variación humana (jitter),
/// para poblar un baseline creíble y evitar el "síndrome de datos robóticos" que dispara falsos positivos.
/// Puro y determinista con semilla → unit-testeable.
/// </summary>
public sealed class SyntheticTrafficGenerator
{
    private readonly Random _rng;

    public SyntheticTrafficGenerator(int? seed = null) => _rng = seed is int s ? new Random(s) : new Random();

    /// <summary>Genera los accesos de <paramref name="days"/> días a partir de <paramref name="start"/> (orden cronológico).</summary>
    public IReadOnlyList<UserAccessSample> Generate(DateTimeOffset start, int days, SyntheticTrafficProfile profile)
    {
        var samples = new List<UserAccessSample>();
        var day0 = new DateTimeOffset(start.Date, start.Offset);

        for (var d = 0; d < days; d++)
        {
            var date = day0.AddDays(d);
            var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var factor = isWeekend ? profile.WeekendActivityFactor : 1.0;

            var count = (int)Math.Round(Math.Max(0, Gaussian(profile.MeanRequestsPerWorkday, profile.MeanRequestsPerWorkday * 0.25)) * factor);

            for (var i = 0; i < count; i++)
            {
                var tod = SampleOfficeTime(profile.OfficeStart, profile.OfficeEnd);
                samples.Add(new UserAccessSample(date + tod, PickEndpoint(profile.Endpoints)));
            }
        }

        samples.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return samples;
    }

    /// <summary>Hora del día bimodal (pico de mañana y de tarde) con pausa de almuerzo, acotada a la jornada.</summary>
    private TimeSpan SampleOfficeTime(TimeSpan start, TimeSpan end)
    {
        var mid = (start + end) / 2;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            // 55% mañana / 45% tarde: dos gaussianas alrededor de los picos típicos.
            var peak = _rng.NextDouble() < 0.55
                ? start + TimeSpan.FromHours(1.5)
                : mid + TimeSpan.FromHours(1.5);

            var hours = Gaussian(peak.TotalHours, 1.1);
            var tod = TimeSpan.FromHours(hours);

            if (tod < start || tod > end)
                continue;

            // Pausa de almuerzo: descartar ~65% de los accesos entre 12:30 y 13:30.
            if (tod >= TimeSpan.FromHours(12.5) && tod <= TimeSpan.FromHours(13.5) && _rng.NextDouble() < 0.65)
                continue;

            return tod;
        }

        return mid; // fallback si el rechazo no converge
    }

    /// <summary>Elige un endpoint sesgado hacia los primeros (distribución tipo Zipf → diversidad realista baja).</summary>
    private string PickEndpoint(IReadOnlyList<string> endpoints)
    {
        var skewed = _rng.NextDouble() * _rng.NextDouble(); // sesga hacia 0
        var index = Math.Min(endpoints.Count - 1, (int)(skewed * endpoints.Count));
        return endpoints[index];
    }

    /// <summary>Muestra gaussiana (Box–Muller) para introducir variación humana.</summary>
    private double Gaussian(double mean, double stdDev)
    {
        var u1 = 1.0 - _rng.NextDouble();
        var u2 = 1.0 - _rng.NextDouble();
        var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mean + z * stdDev;
    }
}
