namespace Infrastructure.GeoLocation;

/// <summary>
/// Opciones del servicio de geolocalización (limpieza: saca la URL y el timeout
/// del hardcode del registro DI hacia configuración tipada).
/// </summary>
public sealed class GeoLocationOptions
{
    public const string SectionName = "GeoLocation";

    /// <summary>URL base de la API de geolocalización. Default: ip-api.com.</summary>
    public string BaseUrl { get; set; } = "http://ip-api.com/";

    /// <summary>Timeout de la petición HTTP, en segundos. Default: 2.</summary>
    public int TimeoutSeconds { get; set; } = 2;
}
