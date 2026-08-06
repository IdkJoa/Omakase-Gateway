using System.Net.Http.Json;
using Application.Common.Security;
using Domain.Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.GeoLocation;

/// <summary>
/// IP geolocation via the free ip-api.com endpoint (T-021).
/// Caches successful results in-memory to avoid repeated lookups for the
/// same IP, and returns a failed <see cref="Result{GeoResult}"/> on timeout/error.
/// </summary>
public sealed class GeoLocationService : IGeoLocationService
{
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GeoLocationService> _logger;
    private readonly TimeSpan _cacheTtl;
    private readonly TimeSpan _failureCacheTtl;

    public GeoLocationService(
        HttpClient http,
        IMemoryCache cache,
        IOptions<GeoLocationOptions> options,
        ILogger<GeoLocationService> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
        _cacheTtl = TimeSpan.FromHours(options.Value.CacheTtlHours);
        _failureCacheTtl = TimeSpan.FromSeconds(options.Value.FailureCacheSeconds);
    }

    public async Task<Result<GeoResult>> ResolveAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return GeoErrors.Unavailable;

        if (_cache.TryGetValue(CacheKey(ipAddress), out GeoResult? cached) && cached is not null)
            return cached;

        // Caché NEGATIVA: solo se cacheaban los aciertos, así que una IP irresoluble volvía a
        // salir a la red en CADA lookup. Y una sola evaluación resuelve la misma IP hasta cuatro
        // veces (comprobación de degradación y desglose de auditoría en EvaluateRiskHandler, más
        // Geofence y Viaje Imposible), de modo que con ip-api.com limitando por tasa cada petición
        // pagaba cuatro timeouts encadenados.
        //
        // Medido en vivo (2026-08-05) durante una corrida de carga que agotó la cuota gratuita de
        // ip-api: el p95 de evaluación pasó de ~10 ms a 3.081 ms — 60x el presupuesto de 50 ms del
        // requisito de rendimiento. Con el fallo cacheado, la degradación cuesta un lookup por IP
        // cada FailureCacheSeconds en vez de uno por regla y por petición.
        if (_cache.TryGetValue<bool>(FailureCacheKey(ipAddress), out _))
            return GeoErrors.Unavailable;

        try
        {
            // El filtro de fields mantiene la respuesta mínima y rápida.
            var dto = await _http.GetFromJsonAsync<IpApiResponse>(
                $"json/{ipAddress}?fields=status,message,countryCode,city,lat,lon",
                cancellationToken);

            if (dto is null || !string.Equals(dto.Status, "success", StringComparison.OrdinalIgnoreCase)
                            || string.IsNullOrWhiteSpace(dto.CountryCode))
            {
                _logger.LogWarning("Geolocation lookup failed for {Ip}: {Message}", ipAddress, dto?.Message ?? "no data");
                CacheFailure(ipAddress);
                return GeoErrors.Unavailable;
            }

            var result = new GeoResult(dto.CountryCode, dto.City, dto.Lat, dto.Lon);
            _cache.Set(CacheKey(ipAddress), result, _cacheTtl);
            return result;
        }
        catch (Exception ex)
        {
            // Fail-safe (RF-M9): CUALQUIER problema al resolver degrada a "no disponible" y el
            // caller decide. Antes solo se capturaban errores de transporte, así que una respuesta
            // malformada de ip-api (JsonException) se propagaba hasta GeofenceRuleEvaluator —que no
            // tiene try/catch, ni lo tiene el bucle de reglas— y tumbaba la evaluación entera con 500.
            _logger.LogWarning(ex, "Geolocation lookup error for {Ip}", ipAddress);
            CacheFailure(ipAddress);
            return GeoErrors.Unavailable;
        }
    }

    /// <summary>Marca la IP como irresoluble por una ventana corta (ver <c>FailureCacheSeconds</c>).</summary>
    private void CacheFailure(string ipAddress) =>
        _cache.Set(FailureCacheKey(ipAddress), true, _failureCacheTtl);

    private static string CacheKey(string ip) => $"geo:{ip}";

    private static string FailureCacheKey(string ip) => $"geo:fail:{ip}";

    /// <summary>Minimal projection of the ip-api.com JSON response.</summary>
    private sealed record IpApiResponse(
        string? Status,
        string? Message,
        string? CountryCode,
        string? City,
        double Lat,
        double Lon);
}
