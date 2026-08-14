using Domain.Common;

namespace Application.Common.Security;

public sealed record GeoResult(string CountryCode, string? City, double Latitude, double Longitude);

public static class GeoErrors
{
    public static readonly Error Unavailable =
        new("Geo.Unavailable", "No se pudo resolver la geolocalización de la IP.");
}

public interface IGeoLocationService
{
    // Returns a failed Result on lookup failure instead of throwing, so callers apply their own
    // fail-safe handling explicitly.
    Task<Result<GeoResult>> ResolveAsync(string ipAddress, CancellationToken cancellationToken = default);
}
