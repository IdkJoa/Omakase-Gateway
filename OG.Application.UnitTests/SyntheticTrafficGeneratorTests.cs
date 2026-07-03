using Application.Common.RiskEngine;
using Application.Common.RiskEngine.AnomalyDetection;
using Infrastructure.AnomalyDetection;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas del generador de tráfico sintético (HU-016 / T-089): valida que la distribución es creíble
/// (horas de oficina, menos fin de semana, diversidad baja) y que el baseline generado entrena un modelo
/// que separa una ráfaga anómala — el "anti falsos positivos" del SRS.
/// </summary>
public class SyntheticTrafficGeneratorTests
{
    private const int Seed = 7;
    private static readonly DateTimeOffset Start = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero); // lunes

    private static IReadOnlyList<UserAccessSample> Generate(int days = 30) =>
        new SyntheticTrafficGenerator(Seed).Generate(Start, days, SyntheticTrafficProfile.OfficeWorker);

    [Fact]
    public void Concentrates_Accesses_In_Office_Hours()
    {
        var accesses = Generate();

        Assert.NotEmpty(accesses);
        var inOffice = accesses.Count(a => a.Timestamp.TimeOfDay >= TimeSpan.FromHours(9)
                                        && a.Timestamp.TimeOfDay <= TimeSpan.FromHours(18));

        Assert.True(inOffice / (double)accesses.Count > 0.95);
    }

    [Fact]
    public void Weekends_Are_Much_Quieter_Than_Weekdays()
    {
        var accesses = Generate();

        double WeekdayAvg(Func<DayOfWeek, bool> pick, int dayCount)
            => accesses.Count(a => pick(a.Timestamp.DayOfWeek)) / (double)dayCount;

        var weekdayAvg = WeekdayAvg(d => d is not (DayOfWeek.Saturday or DayOfWeek.Sunday), 22); // ~22 días hábiles en 30
        var weekendAvg = WeekdayAvg(d => d is DayOfWeek.Saturday or DayOfWeek.Sunday, 8);

        Assert.True(weekendAvg < weekdayAvg * 0.5, $"weekend={weekendAvg:F1} vs weekday={weekdayAvg:F1}");
    }

    [Fact]
    public void Diversity_Is_Realistically_Low_NotRobotic()
    {
        var accesses = Generate();

        var distinct = accesses.Select(a => a.Endpoint).Distinct().Count();

        Assert.True(distinct <= SyntheticTrafficProfile.OfficeWorker.Endpoints.Count);
        Assert.True(accesses.Count > distinct * 10, "muchos accesos sobre pocos endpoints (diversidad baja)");
    }

    [Fact]
    public void Generated_Baseline_Trains_A_Model_That_Separates_A_Burst()
    {
        var options = new AnomalyDetectionOptions { PcaRank = 3, MinTrainingSamples = 20 };
        var extractor = new FeatureExtractor(options);
        var accesses = Generate(days: 20);

        // Convertir el stream sintético en la ventana de features (cada acceso ve su historial previo).
        var window = new List<AnomalyFeatureVector>();
        for (var i = 0; i < accesses.Count; i++)
        {
            var ctx = new RequestContext { SourceIp = string.Empty, Timestamp = accesses[i].Timestamp };
            window.Add(extractor.Extract(ctx, accesses.Take(i).ToList()));
        }

        var model = new AnomalyModelTrainer(options, Seed).Train(window);

        var normal = model.Score(new AnomalyFeatureVector(0.5f, 0.1f, 0.10f, 0.20f)); // día laboral tranquilo
        var burst = model.Score(new AnomalyFeatureVector(0.85f, 0.85f, 0.95f, 0.90f)); // ráfaga

        Assert.True(burst > normal, $"burst={burst:F1} debería superar normal={normal:F1}");
    }
}
