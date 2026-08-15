using System.Net.Http.Json;
using Application.Common.Security;
using Domain.Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.GeoLocation;

// IP geolocation via ip-api.com; caches successes in-memory and returns a failed Result on timeout/error.
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

        // Caché negativa: sin ella cada evaluación repite hasta 4 lookups de la misma IP irresoluble;
        // medido en vivo (2026-08-05) esto llevó el p95 de ~10ms a 3081ms bajo rate-limiting de ip-api.
        if (_cache.TryGetValue<bool>(FailureCacheKey(ipAddress), out _))
            return GeoErrors.Unavailable;

        try
        {
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
            // Fail-safe (RF-M9): captura CUALQUIER error, no solo de transporte — un JsonException antes
            // se propagaba sin manejar hasta GeofenceRuleEvaluator y tumbaba la evaluación con 500.
            _logger.LogWarning(ex, "Geolocation lookup error for {Ip}", ipAddress);
            CacheFailure(ipAddress);
            return GeoErrors.Unavailable;
        }
    }

    private void CacheFailure(string ipAddress) =>
        _cache.Set(FailureCacheKey(ipAddress), true, _failureCacheTtl);

    private static string CacheKey(string ip) => $"geo:{ip}";

    private static string FailureCacheKey(string ip) => $"geo:fail:{ip}";

    private sealed record IpApiResponse(
        string? Status,
        string? Message,
        string? CountryCode,
        string? City,
        double Lat,
        double Lon);
}
