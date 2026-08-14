using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Infrastructure.GeoLocation;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace OG.Application.UnitTests;

// Cliente de geolocalización (T-021): caché negativa y fail-safe total (RF-M9). Sin cachear el fallo,
// una IP irresoluble cuesta 4 salidas de red por evaluación (Geofence + Viaje Imposible + auditoría);
// medido en vivo, el p95 subió de ~10ms a 3.081ms cuando ip-api.com agotó su cuota bajo carga.
public class GeoLocationServiceTests
{
    /// <summary>Handler que cuenta las llamadas y devuelve siempre la misma respuesta programada.</summary>
    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _responder;

        public CountingHandler(Func<HttpResponseMessage> responder) => _responder = responder;

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_responder());
        }
    }

    private static GeoLocationService CreateSut(CountingHandler handler, int failureCacheSeconds = 30) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://ip-api.test/") },
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new GeoLocationOptions { FailureCacheSeconds = failureCacheSeconds }),
            NullLogger<GeoLocationService>.Instance);

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task IpIrresoluble_CacheaElFallo_YNoRepiteLaLlamadaDeRed()
    {
        // Dado que ip-api responde "fail" (cuota agotada o IP privada)
        var handler = new CountingHandler(() => Json("""{"status":"fail","message":"reserved range"}"""));
        var sut = CreateSut(handler);

        // Cuando la misma IP se resuelve cuatro veces, como hace una sola evaluación
        for (var i = 0; i < 4; i++)
            Assert.True((await sut.ResolveAsync("10.0.0.1")).IsFailure);

        // Entonces solo se sale a la red UNA vez: las otras tres las sirve la caché negativa
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ErrorDeTransporte_TambienSeCachea()
    {
        var handler = new CountingHandler(() => throw new HttpRequestException("connection refused"));
        var sut = CreateSut(handler);

        Assert.True((await sut.ResolveAsync("190.166.12.45")).IsFailure);
        Assert.True((await sut.ResolveAsync("190.166.12.45")).IsFailure);

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RespuestaMalformada_DegradaAFallo_EnVezDePropagarLaExcepcion()
    {
        // Antes solo se capturaban errores de transporte, así que un JSON corrupto lanzaba
        // JsonException, que subía hasta GeofenceRuleEvaluator —sin try/catch, igual que el bucle
        // de reglas del handler— y tumbaba la evaluación entera con un 500.
        var handler = new CountingHandler(() => Json("esto no es json"));
        var sut = CreateSut(handler);

        var result = await sut.ResolveAsync("190.166.12.45");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task FalloCacheado_NoContaminaLaResolucionDeOtrasIps()
    {
        var fallan = true;
        var handler = new CountingHandler(() => fallan
            ? Json("""{"status":"fail"}""")
            : Json("""{"status":"success","countryCode":"DO","city":"Santo Domingo","lat":18.46,"lon":-69.89}"""));
        var sut = CreateSut(handler);

        Assert.True((await sut.ResolveAsync("10.0.0.1")).IsFailure);

        // Otra IP no hereda el fallo: la caché negativa es por clave, no global.
        fallan = false;
        var ok = await sut.ResolveAsync("190.166.12.45");

        Assert.True(ok.IsSuccess);
        Assert.Equal("DO", ok.Value.CountryCode);
    }

    [Fact]
    public async Task ExitoSeCachea_YNoRepiteLaLlamadaDeRed()
    {
        var handler = new CountingHandler(() =>
            Json("""{"status":"success","countryCode":"DO","city":"Santo Domingo","lat":18.46,"lon":-69.89}"""));
        var sut = CreateSut(handler);

        for (var i = 0; i < 4; i++)
            Assert.True((await sut.ResolveAsync("190.166.12.45")).IsSuccess);

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task IpVacia_NoSaleALaRed()
    {
        var handler = new CountingHandler(() => Json("""{"status":"success","countryCode":"DO"}"""));
        var sut = CreateSut(handler);

        Assert.True((await sut.ResolveAsync("  ")).IsFailure);
        Assert.Equal(0, handler.Calls);
    }
}
