using Domain.Common;

namespace Application.Common.Security;

/// <summary>
/// Geographic resolution of a source IP address.
/// </summary>
public sealed record GeoResult(string CountryCode, string? City, double Latitude, double Longitude);

/// <summary>Errors produced by the geolocation service.</summary>
public static class GeoErrors
{
    /// <summary>The location could not be resolved (timeout, error or unresolvable address).</summary>
    public static readonly Error Unavailable =
        new("Geo.Unavailable", "No se pudo resolver la geolocalización de la IP.");
}

/// <summary>
/// Resolves the geographic origin of an IP address against an external
/// geolocation API (RF-M2 / T-021).
/// </summary>
public interface IGeoLocationService
{
    /// <summary>
    /// Resolves the geolocation of an IP. Returns a failed <see cref="Result{GeoResult}"/>
    /// (<see cref="GeoErrors.Unavailable"/>) when the lookup cannot be completed, so
    /// callers apply their fail-safe handling explicitly.
    /// </summary>
    Task<Result<GeoResult>> ResolveAsync(string ipAddress, CancellationToken cancellationToken = default);
}
