using Application.Common.RiskEngine;
using Application.Common.RiskEngine.AnomalyDetection;
using Infrastructure.AnomalyDetection;
using NSubstitute;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Pruebas del detector real (HU-016 / T-032+T-033): degradación segura (sin identidad / cold-start →
/// incertidumbre 50, RF-M9) y scoring por usuario contra su perfil (normal bajo, ráfaga alta).
/// </summary>
public class RandomizedPcaAnomalyDetectorTests
{
    private const int Seed = 7;
    private const string UserId = "user-1";

    private readonly AnomalyDetectionOptions _options = new() { PcaRank = 3, MinTrainingSamples = 20 };
    private readonly IUserProfileStore _store = Substitute.For<IUserProfileStore>();
    private readonly RandomizedPcaAnomalyDetector _sut;

    public RandomizedPcaAnomalyDetectorTests()
    {
        _sut = new RandomizedPcaAnomalyDetector(
            _store, new FeatureExtractor(_options), new AnomalyModelTrainer(_options, Seed),
            new AnomalyModelCache(), _options);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static RequestContext Request(int hour, string? userId = UserId) => new()
    {
        SourceIp = "203.0.113.7",
        UserId = userId,
        Timestamp = new DateTimeOffset(2026, 7, 3, hour, 0, 0, TimeSpan.Zero),
    };

    private static AnomalyFeatureVector Vec(double hour, float freq, float div)
    {
        var rad = 2.0 * Math.PI * hour / 24.0;
        return new AnomalyFeatureVector((float)((Math.Sin(rad) + 1) / 2), (float)((Math.Cos(rad) + 1) / 2), freq, div);
    }

    private static List<AnomalyFeatureVector> OfficeBaseline(int count = 300)
    {
        var rng = new Random(Seed);
        var list = new List<AnomalyFeatureVector>(count);
        for (var i = 0; i < count; i++)
            list.Add(Vec(9 + rng.NextDouble() * 8, 0.05f + (float)rng.NextDouble() * 0.20f, 0.10f + (float)rng.NextDouble() * 0.20f));
        return list;
    }

    private static List<UserAccessSample> RecentAccesses(DateTimeOffset now, int count, int distinctEndpoints)
    {
        var list = new List<UserAccessSample>(count);
        for (var i = 0; i < count; i++)
            list.Add(new UserAccessSample(now.AddMinutes(-((i % 50) + 1)), $"/e{i % distinctEndpoints}"));
        return list;
    }

    private void GivenProfile(IReadOnlyList<AnomalyFeatureVector> window, IReadOnlyList<UserAccessSample> recent) =>
        _store.GetAsync(UserId, Arg.Any<CancellationToken>())
              .Returns(Task.FromResult<UserAnomalyProfile?>(
                  new UserAnomalyProfile(window, recent, AccessCount: window.Count, IsColdStart: false)));

    // ── Degradación segura ─────────────────────────────────────────────────────

    [Fact]
    public async Task ReturnsNeutral_WhenNoUserIdentity()
    {
        var score = await _sut.GetAnomalyScoreAsync(Request(13, userId: null));
        Assert.Equal(50m, score);
    }

    [Fact]
    public async Task ReturnsNeutral_WhenNoProfileExists()
    {
        _store.GetAsync(UserId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<UserAnomalyProfile?>(null));

        var score = await _sut.GetAnomalyScoreAsync(Request(13));

        Assert.Equal(50m, score);
    }

    [Fact]
    public async Task ReturnsNeutral_WhenColdStart_InsufficientHistory()
    {
        GivenProfile(OfficeBaseline(count: 5), RecentAccesses(Request(13).Timestamp, 5, 2));

        var score = await _sut.GetAnomalyScoreAsync(Request(13));

        Assert.Equal(50m, score);
    }

    // ── Scoring por usuario ─────────────────────────────────────────────────────

    [Fact]
    public async Task ScoresNormalRequest_Low()
    {
        var ctx = Request(13);
        GivenProfile(OfficeBaseline(), RecentAccesses(ctx.Timestamp, count: 10, distinctEndpoints: 2)); // freq~0.17, div~0.2

        var score = await _sut.GetAnomalyScoreAsync(ctx);

        Assert.True(score < 50m, $"un patrón normal debería puntuar bajo, fue {score:F1}");
    }

    [Fact]
    public async Task ScoresBurst_High_AndAboveNormal()
    {
        var ctx = Request(13);

        GivenProfile(OfficeBaseline(), RecentAccesses(ctx.Timestamp, count: 10, distinctEndpoints: 2));
        var normal = await _sut.GetAnomalyScoreAsync(ctx);

        // Ráfaga: 60 accesos recientes, muchos endpoints → frecuencia/diversidad altas.
        GivenProfile(OfficeBaseline(), RecentAccesses(ctx.Timestamp, count: 60, distinctEndpoints: 30));
        var burst = await _sut.GetAnomalyScoreAsync(ctx);

        Assert.True(burst > 60m, $"una ráfaga debería puntuar alto, fue {burst:F1}");
        Assert.True(burst > normal, $"burst={burst:F1} debería superar normal={normal:F1}");
    }

    // ── Inicio de sesión: sin actividad en la ventana no hay volumen que medir ──

    /// <summary>
    /// Regresión de un falso positivo verificado en vivo (2026-08-01): el usuario que empieza una
    /// sesión no tiene accesos en la ventana de frecuencia, así que frecuencia y diversidad quedan
    /// INDEFINIDAS y el extractor las emite como (0,0). Ese vector cae en un extremo donde el baseline
    /// casi no tiene puntos, y el modelo lo marcaba como anomalía extrema (se midieron 89.5 y hasta
    /// 99.5) — dejando al acceso legítimo pegado a la ráfaga de ataque (98.0) e impidiendo calibrar.
    /// Ahora se devuelve incertidumbre, igual que en cold-start.
    /// </summary>
    [Fact]
    public async Task ReturnsNeutral_CuandoNoHayActividadEnLaVentanaDeFrecuencia()
    {
        var ctx = Request(13);

        // Accesos reales pero TODOS anteriores a la ventana de 60 min (ayer).
        var antiguos = RecentAccesses(ctx.Timestamp.AddDays(-1), count: 40, distinctEndpoints: 5);
        GivenProfile(OfficeBaseline(), antiguos);

        var score = await _sut.GetAnomalyScoreAsync(ctx);

        Assert.Equal(50m, score);
    }

    [Fact]
    public async Task ReturnsNeutral_CuandoNoHayNingunAccesoReciente()
    {
        var ctx = Request(13);
        GivenProfile(OfficeBaseline(), Array.Empty<UserAccessSample>());

        var score = await _sut.GetAnomalyScoreAsync(ctx);

        Assert.Equal(50m, score);
    }

    /// <summary>
    /// Regresión del segundo falso positivo medido en vivo (2026-08-01): con UN solo acceso previo en
    /// la ventana, la diversidad vale forzosamente 1.0 y el usuario legítimo puntuaba 92.5 — su segunda
    /// petición de la hora habría sido desafiada. Por debajo del mínimo de muestras se degrada.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task ReturnsNeutral_CuandoLaVentanaNoAlcanzaElMinimoDeMuestras(int accesos)
    {
        var ctx = Request(13);
        var recientes = Enumerable.Range(1, accesos)
            .Select(i => new UserAccessSample(ctx.Timestamp.AddMinutes(-i), "/reports"))
            .ToList();
        GivenProfile(OfficeBaseline(), recientes);

        var score = await _sut.GetAnomalyScoreAsync(ctx);

        Assert.Equal(50m, score);
    }

    /// <summary>
    /// El guard no puede tapar la detección: alcanzado el mínimo, se vuelve a puntuar con el modelo.
    /// Un ataque por volumen (50 peticiones) lo supera con holgura.
    /// </summary>
    [Fact]
    public async Task AlAlcanzarElMinimoDeMuestras_VuelveAPuntuarConElModelo()
    {
        var ctx = Request(13);
        GivenProfile(OfficeBaseline(), RecentAccesses(ctx.Timestamp, count: _options.MinWindowSamples, distinctEndpoints: 2));

        var score = await _sut.GetAnomalyScoreAsync(ctx);

        Assert.NotEqual(50m, score);
    }
}
