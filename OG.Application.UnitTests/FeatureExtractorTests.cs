using Application.Common.RiskEngine;
using Application.Common.RiskEngine.AnomalyDetection;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas del FeatureExtractor (HU-016 / T-031): codificación cíclica de la hora,
/// frecuencia y diversidad, todo normalizado a [0,1]. Componente puro y determinista.
/// </summary>
public class FeatureExtractorTests
{
    private readonly FeatureExtractor _sut = new(new AnomalyDetectionOptions());

    private static RequestContext At(int hour, int minute = 0) => new()
    {
        SourceIp = "203.0.113.7",
        Timestamp = new DateTimeOffset(2026, 7, 3, hour, minute, 0, TimeSpan.Zero),
    };

    private static UserAccessSample Access(DateTimeOffset ts, string endpoint) => new(ts, endpoint);

    // ── Codificación de la hora (sin/cos → [0,1]) ────────────────────────────

    [Theory]
    [InlineData(0, 0.5, 1.0)]    // 00:00 → sin 0, cos 1
    [InlineData(6, 1.0, 0.5)]    // 06:00 → sin 1, cos 0
    [InlineData(12, 0.5, 0.0)]   // 12:00 → sin 0, cos -1
    [InlineData(18, 0.0, 0.5)]   // 18:00 → sin -1, cos 0
    public void EncodeHour_MapsToExpectedNormalisedSinCos(int hour, double expectedSin, double expectedCos)
    {
        var v = _sut.Extract(At(hour), []);

        Assert.Equal(expectedSin, v.HourSin, 4);
        Assert.Equal(expectedCos, v.HourCos, 4);
    }

    [Fact]
    public void EncodeHour_Is_Cyclic_23h_Adjacent_To_00h()
    {
        var a = _sut.Extract(At(23), []);
        var b = _sut.Extract(At(0), []);
        var far = _sut.Extract(At(6), []);

        // 23:00 y 00:00 deben quedar mucho más cerca entre sí que 00:00 respecto a 06:00.
        Assert.True(Distance(a, b) < Distance(b, far));
    }

    // ── Frecuencia ────────────────────────────────────────────────────────────

    [Fact]
    public void Frequency_And_Diversity_AreZero_When_NoHistory()
    {
        var v = _sut.Extract(At(12), []);

        Assert.Equal(0f, v.Frequency);
        Assert.Equal(0f, v.Diversity);
    }

    [Fact]
    public void Frequency_Counts_Only_Accesses_Within_The_Window()
    {
        var now = At(12);
        var accesses = new[]
        {
            Access(now.Timestamp.AddMinutes(-10), "/a"),  // dentro
            Access(now.Timestamp.AddMinutes(-30), "/a"),  // dentro
            Access(now.Timestamp.AddMinutes(-90), "/a"),  // fuera (>60 min)
        };

        var v = _sut.Extract(now, accesses);

        // 2 dentro de la ventana / saturación 60 = 0.0333…
        Assert.Equal(2f / 60f, v.Frequency, 4);
    }

    [Fact]
    public void Frequency_Saturates_At_One()
    {
        var now = At(12);
        var saturating = new FeatureExtractor(new AnomalyDetectionOptions { FrequencySaturation = 5 });
        var accesses = Enumerable.Range(1, 20)
            .Select(i => Access(now.Timestamp.AddMinutes(-i), "/a"))
            .ToArray();

        var v = saturating.Extract(now, accesses);

        Assert.Equal(1f, v.Frequency);
    }

    // ── Diversidad (únicos / total) ─────────────────────────────────────────────

    [Fact]
    public void Diversity_Is_Low_When_All_Accesses_Hit_Same_Endpoint()
    {
        var now = At(12);
        var accesses = Enumerable.Range(1, 4)
            .Select(i => Access(now.Timestamp.AddMinutes(-i), "/same"))
            .ToArray();

        var v = _sut.Extract(now, accesses);

        Assert.Equal(1f / 4f, v.Diversity, 4);   // 1 único / 4 total
    }

    [Fact]
    public void Diversity_Is_One_When_Every_Endpoint_Is_Unique()
    {
        var now = At(12);
        var accesses = new[]
        {
            Access(now.Timestamp.AddMinutes(-1), "/a"),
            Access(now.Timestamp.AddMinutes(-2), "/b"),
            Access(now.Timestamp.AddMinutes(-3), "/c"),
        };

        var v = _sut.Extract(now, accesses);

        Assert.Equal(1f, v.Diversity);
    }

    // ── Invariante global: todo en [0,1] ────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(9)]
    [InlineData(15)]
    [InlineData(21)]
    public void All_Features_Stay_Within_Unit_Range(int hour)
    {
        var now = At(hour);
        var accesses = Enumerable.Range(1, 200)
            .Select(i => Access(now.Timestamp.AddMinutes(-(i % 55)), $"/e{i % 7}"))
            .ToArray();

        var v = _sut.Extract(now, accesses);

        foreach (var f in v.ToArray())
        {
            Assert.InRange(f, 0f, 1f);
        }
    }

    private static double Distance(AnomalyFeatureVector a, AnomalyFeatureVector b)
    {
        var ds = a.HourSin - b.HourSin;
        var dc = a.HourCos - b.HourCos;
        return Math.Sqrt(ds * ds + dc * dc);
    }
}
