using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Proxy;

/// <summary>
/// Recargador dinámico de rutas de YARP (HU-009 / T-018).
/// Sondea <c>protected_services</c> cada <see cref="ProxyReloadOptions.IntervalSeconds"/> segundos
/// y actualiza el <see cref="DatabaseProxyConfigProvider"/>; los cambios de <c>upstream_url</c> o
/// <c>is_active</c> se aplican sin reiniciar el Gateway.
/// </summary>
/// <remarks>
/// El provider es Singleton pero <see cref="OmakaseDbContext"/> es Scoped: se crea un scope por
/// ciclo con <see cref="IServiceScopeFactory"/> para respetar los ciclos de vida del contenedor DI
/// (mismo patrón que <c>AuditPersistenceWorker</c>). No usa el mediador: cargar la config de YARP es
/// plumbing de infraestructura, no un caso de uso del pipeline.
/// </remarks>
public sealed class ProxyConfigReloader : BackgroundService
{
    private readonly DatabaseProxyConfigProvider _provider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProxyConfigReloader> _logger;
    private readonly TimeSpan _interval;

    public ProxyConfigReloader(
        DatabaseProxyConfigProvider provider,
        IServiceScopeFactory scopeFactory,
        IOptions<ProxyReloadOptions> options,
        ILogger<ProxyConfigReloader> logger)
    {
        _provider = provider;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(options.Value.IntervalSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "[ProxyConfigReloader] Iniciado. Intervalo de recarga: {Interval}s.", _interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Recarga inmediata en el primer ciclo para hidratar las rutas al arranque.
            try
            {
                await ReloadAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ProxyConfigReloader] Error al recargar rutas desde protected_services.");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("[ProxyConfigReloader] Detenido.");
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OmakaseDbContext>();

        var services = await db.ProtectedServices
            .AsNoTracking()
            .Where(s => s.IsActive)
            .ToListAsync(cancellationToken);

        _provider.Update(services);

        _logger.LogInformation(
            "[ProxyConfigReloader] {Count} servicio(s) activo(s) hidratado(s) en YARP.", services.Count);
    }
}
