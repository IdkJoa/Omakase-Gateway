using Application.Common.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Proxy;

/// <summary>
/// Recargador dinámico de rutas de YARP (HU-009 / T-018 y HU-024).
/// Sondea <c>protected_services</c> periódicamente o cuando recibe una señal
/// vía Redis Pub/Sub, y actualiza el <see cref="DatabaseProxyConfigProvider"/>;
/// los cambios de <c>upstream_url</c> o <c>is_active</c> se aplican sin reiniciar el Gateway.
/// </summary>
public sealed class ProxyConfigReloader : BackgroundService
{
    private readonly DatabaseProxyConfigProvider _provider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRedisService _redis;
    private readonly ILogger<ProxyConfigReloader> _logger;
    private readonly TimeSpan _interval;
    private readonly SemaphoreSlim _reloadSignal = new(0, 1);

    public ProxyConfigReloader(
        DatabaseProxyConfigProvider provider,
        IServiceScopeFactory scopeFactory,
        IRedisService redis,
        IOptions<ProxyReloadOptions> options,
        ILogger<ProxyConfigReloader> logger)
    {
        _provider = provider;
        _scopeFactory = scopeFactory;
        _redis = redis;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(options.Value.IntervalSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "[ProxyConfigReloader] Iniciado. Intervalo de recarga: {Interval}s. Escuchando Redis...", _interval.TotalSeconds);

        try
        {
            await _redis.SubscribeAsync("yarp-reload-channel", (channel, message) =>
            {
                _logger.LogInformation("[ProxyConfigReloader] Señal de recarga recibida por Redis Pub/Sub.");
                // Release si no hay un semáforo disponible ya, para evitar múltiples reloads simultáneos.
                if (_reloadSignal.CurrentCount == 0)
                {
                    _reloadSignal.Release();
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ProxyConfigReloader] No se pudo suscribir al canal de Redis. Se continuará con polling estándar.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
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
                // Espera hasta que pasen los N segundos o llegue una señal de Redis
                await _reloadSignal.WaitAsync(_interval, stoppingToken);
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
