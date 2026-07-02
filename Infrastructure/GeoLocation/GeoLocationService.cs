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
    }

    public async Task<Result<GeoResult>> ResolveAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return GeoErrors.Unavailable;

        if (_cache.TryGetValue(CacheKey(ipAddress), out GeoResult? cached) && cached is not null)
            return cached;

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
                return GeoErrors.Unavailable;
            }

            var result = new GeoResult(dto.CountryCode, dto.City, dto.Lat, dto.Lon);
            _cache.Set(CacheKey(ipAddress), result, _cacheTtl);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // Timeout o error de transporte → fail-safe: decide el caller (RF-M9).
            _logger.LogWarning(ex, "Geolocation lookup error for {Ip}", ipAddress);
            return GeoErrors.Unavailable;
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
