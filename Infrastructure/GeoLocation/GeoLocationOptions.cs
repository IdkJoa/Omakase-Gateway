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

    /// <summary>Vigencia de la caché de resultados por IP, en horas. Default: 1.</summary>
    public int CacheTtlHours { get; set; } = 1;

    /// <summary>
    /// Vigencia de la caché <b>negativa</b> (IPs que no se pudieron resolver), en segundos.
    /// Default: 30. Corta a la vez el coste y la duración de una degradación de geolocalización
    /// sin dejar la IP marcada como irresoluble más de lo necesario.
    /// </summary>
    public int FailureCacheSeconds { get; set; } = 30;
}
