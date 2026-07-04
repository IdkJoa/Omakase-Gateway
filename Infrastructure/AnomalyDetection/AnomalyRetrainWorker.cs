using Application.Common.RiskEngine.AnomalyDetection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AnomalyDetection;

/// <summary>
/// Reentrenamiento periódico programado de los modelos de anomalías (HU-017 / T-035). Cada
/// <see cref="AnomalyDetectionOptions.RetrainIntervalHours"/> horas invalida la caché de modelos —de modo
/// que se reconstruyan con la ventana de features más reciente en la siguiente evaluación— y actualiza
/// <c>last_trained_at</c> en <c>user_behavior_profiles</c>.
/// <para><b>Singleton</b>; crea un scope para el <c>DbContext</c> (Scoped) en cada ciclo.</para>
/// </summary>
public sealed class AnomalyRetrainWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAnomalyModelCache _modelCache;
    private readonly AnomalyDetectionOptions _options;
    private readonly ILogger<AnomalyRetrainWorker> _logger;

    public AnomalyRetrainWorker(
        IServiceScopeFactory scopeFactory,
        IAnomalyModelCache modelCache,
        AnomalyDetectionOptions options,
        ILogger<AnomalyRetrainWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _modelCache = modelCache;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(1, _options.RetrainIntervalHours));
        _logger.LogInformation("[AnomalyRetrainWorker] Iniciado. Intervalo={Hours}h.", interval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
                await RetrainAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AnomalyRetrainWorker] Error en el ciclo de reentrenamiento.");
            }
        }

        _logger.LogInformation("[AnomalyRetrainWorker] Detenido.");
    }

    private async Task RetrainAsync(CancellationToken cancellationToken)
    {
        // Invalida los modelos cacheados: la próxima evaluación los reconstruye desde la ventana fresca.
        _modelCache.Clear();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OmakaseDbContext>();

        var now = DateTimeOffset.UtcNow;
        var updated = await db.UserBehaviorProfiles
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastTrainedAt, now), cancellationToken);

        _logger.LogInformation(
            "[AnomalyRetrainWorker] Reentrenamiento marcado: caché limpiada, {Count} perfil(es) actualizados.", updated);
    }
}
