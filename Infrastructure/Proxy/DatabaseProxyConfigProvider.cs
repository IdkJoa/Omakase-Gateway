using Domain.Entities;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace Infrastructure.Proxy;

/// <summary>
/// Proveedor de configuración de YARP hidratado desde <c>protected_services</c> (HU-009 / T-017).
/// <para>
/// Cada servicio activo (<see cref="ProtectedService.IsActive"/>) se mapea a un cluster con su
/// <see cref="ProtectedService.UpstreamUrl"/> y a una ruta por convención de prefijo de path:
/// <c>/{name}/{**catch-all}</c> → cluster <c>{name}</c>. protected_services no define el patrón de
/// ruta, por lo que se usa el prefijo por nombre (estándar de gateways).
/// </para>
/// <para>
/// El <see cref="ProxyConfigReloader"/> (T-018) llama a <see cref="Update"/> periódicamente; el
/// intercambio del <see cref="IProxyConfig"/> y la señal del <see cref="IChangeToken"/> hacen que
/// YARP recargue sin reiniciar el proceso.
/// </para>
/// </summary>
public sealed class DatabaseProxyConfigProvider : IProxyConfigProvider
{
    private volatile DatabaseProxyConfig _config = new([], []);

    public IProxyConfig GetConfig() => _config;

    /// <summary>
    /// Reconstruye rutas y clusters a partir de los servicios activos y activa la recarga de YARP.
    /// </summary>
    public void Update(IReadOnlyList<ProtectedService> services)
    {
        var routes = new List<RouteConfig>(services.Count);
        var clusters = new List<ClusterConfig>(services.Count);

        foreach (var service in services)
        {
            routes.Add(new RouteConfig
            {
                RouteId = $"route-{service.Name}",
                ClusterId = service.Name,
                Match = new RouteMatch { Path = $"/{service.Name}/{{**catch-all}}" },
                // Quita el prefijo /{name} antes de reenviar: /{name}/foo -> {upstream}/foo.
                Transforms = new List<IReadOnlyDictionary<string, string>>
                {
                    new Dictionary<string, string> { ["PathRemovePrefix"] = $"/{service.Name}" },
                },
            });

            clusters.Add(new ClusterConfig
            {
                ClusterId = service.Name,
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["primary"] = new DestinationConfig { Address = service.UpstreamUrl },
                },
            });
        }

        // Intercambio atómico + señal al token anterior para que YARP relea la config.
        var previous = _config;
        _config = new DatabaseProxyConfig(routes, clusters);
        previous.SignalChange();
    }

    /// <summary>Snapshot inmutable de la config de YARP con su token de cambio.</summary>
    private sealed class DatabaseProxyConfig : IProxyConfig
    {
        private readonly CancellationTokenSource _cts = new();

        public DatabaseProxyConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
        {
            Routes = routes;
            Clusters = clusters;
            ChangeToken = new CancellationChangeToken(_cts.Token);
        }

        public IReadOnlyList<RouteConfig> Routes { get; }
        public IReadOnlyList<ClusterConfig> Clusters { get; }
        public IChangeToken ChangeToken { get; }

        public void SignalChange() => _cts.Cancel();
    }
}
