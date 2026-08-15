namespace Infrastructure.GeoLocation;

public sealed class GeoLocationOptions
{
    public const string SectionName = "GeoLocation";

    public string BaseUrl { get; set; } = "http://ip-api.com/";

    public int TimeoutSeconds { get; set; } = 2;

    public int CacheTtlHours { get; set; } = 1;

    // Caché negativa: acota el coste/duración de una degradación sin marcar la IP irresoluble por más tiempo del necesario.
    public int FailureCacheSeconds { get; set; } = 30;
}
