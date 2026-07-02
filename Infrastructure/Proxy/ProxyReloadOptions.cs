namespace Infrastructure.Proxy;

/// <summary>
/// Opciones del recargador dinámico de rutas de YARP (HU-009 / T-018).
/// </summary>
public sealed class ProxyReloadOptions
{
    public const string SectionName = "ProxyReload";

    /// <summary>Intervalo de polling a <c>protected_services</c>, en segundos. Default: 30.</summary>
    public int IntervalSeconds { get; set; } = 30;
}
