namespace Application.Common.Security;

/// <summary>
/// Geographic resolution of a source IP address.
/// </summary>
public sealed record GeoResult(string CountryCode, string? City, double Latitude, double Longitude);

/// <summary>
/// Resolves the geographic origin of an IP address against an external
/// geolocation API (RF-M2 / T-021).
/// </summary>
public interface IGeoLocationService
{
    /// <summary>
    /// Resolves the geolocation of an IP. Returns <c>null</c> when the lookup
    /// cannot be completed (timeout, error or unresolvable address), so callers
    /// can apply their fail-safe handling.
    /// </summary>
    Task<GeoResult?> ResolveAsync(string ipAddress, CancellationToken cancellationToken = default);
}
