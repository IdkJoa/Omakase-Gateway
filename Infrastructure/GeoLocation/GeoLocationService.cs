using System.Net.Http.Json;
using Application.Common.Security;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Infrastructure.GeoLocation;

/// <summary>
/// IP geolocation via the free ip-api.com endpoint (T-021).
/// Caches results in-memory (1h) to avoid repeated lookups for the same IP,
/// and returns <c>null</c> on timeout/error so the caller applies its fail-safe.
/// </summary>
public sealed class GeoLocationService : IGeoLocationService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GeoLocationService> _logger;

    public GeoLocationService(HttpClient http, IMemoryCache cache, ILogger<GeoLocationService> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    public async Task<GeoResult?> ResolveAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return null;

        if (_cache.TryGetValue(CacheKey(ipAddress), out GeoResult? cached))
            return cached;

        try
        {
            // fields filter keeps the response minimal and fast.
            var dto = await _http.GetFromJsonAsync<IpApiResponse>(
                $"json/{ipAddress}?fields=status,message,countryCode,city,lat,lon",
                cancellationToken);

            if (dto is null || !string.Equals(dto.Status, "success", StringComparison.OrdinalIgnoreCase)
                            || string.IsNullOrWhiteSpace(dto.CountryCode))
            {
                _logger.LogWarning("Geolocation lookup failed for {Ip}: {Message}", ipAddress, dto?.Message ?? "no data");
                return null;
            }

            var result = new GeoResult(dto.CountryCode, dto.City, dto.Lat, dto.Lon);
            _cache.Set(CacheKey(ipAddress), result, CacheTtl);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // Timeout or transport error → fail-safe: caller decides (RF-M9).
            _logger.LogWarning(ex, "Geolocation lookup error for {Ip}", ipAddress);
            return null;
        }
    }

    private static string CacheKey(string ip) => $"geo:{ip}";

    /// <summary>Minimal projection of the ip-api.com JSON response.</summary>
    private sealed record IpApiResponse(
        string? Status,
        string? Message,
        string? CountryCode,
        string? City,
        double Lat,
        double Lon);
}
