using Application.Common.RiskEngine;
using Application.Common.RiskEngine.AnomalyDetection;
using Application.Common.Security;
using Domain.Entities;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AnomalyDetection;

/// <summary>
/// Worker que drena el <see cref="IProfileUpdateChannel"/> y persiste el perfil de comportamiento en
/// <c>user_behavior_profiles</c> fuera de la ruta crítica (HU-016 T-033 / HU-017 T-034): recompone el vector
/// de features del acceso, lo agrega a la ventana rodante, incrementa <c>access_count</c>, recalcula
/// <c>is_cold_start</c>, guarda <c>base_risk_penalty</c> y refresca la caché Redis.
/// <para><b>Singleton</b> (vive toda la app); crea un scope por mensaje para el <c>DbContext</c> (Scoped).</para>
/// </summary>
public sealed class ProfileUpdateWorker : BackgroundService
{
    private readonly IProfileUpdateChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFeatureExtractor _extractor;
    private readonly IRedisService _redis;
    private readonly AnomalyDetectionOptions _options;
    private readonly ILogger<ProfileUpdateWorker> _logger;

    public ProfileUpdateWorker(
        IProfileUpdateChannel channel,
        IServiceScopeFactory scopeFactory,
        IFeatureExtractor extractor,
        IRedisService redis,
        AnomalyDetectionOptions options,
        ILogger<ProfileUpdateWorker> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _extractor = extractor;
        _redis = redis;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[ProfileUpdateWorker] Iniciado.");

        try
        {
            await foreach (var update in _channel.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await PersistAsync(update, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Fail-safe: una actualización perdida no debe tumbar el worker ni la evaluación.
                    _logger.LogError(ex, "[ProfileUpdateWorker] Error actualizando perfil de {UserId}.", update.UserId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Apagado limpio.
        }

        _logger.LogInformation("[ProfileUpdateWorker] Detenido.");
    }

    private async Task PersistAsync(ProfileUpdate update, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(update.UserId, out var guid))
            return;

        var domainId = new UserId(guid);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OmakaseDbContext>();

        var entity = await db.UserBehaviorProfiles
            .FirstOrDefaultAsync(p => p.UserId == domainId, cancellationToken);

        var isNew = entity is null;
        entity ??= new UserBehaviorProfile { Id = UserBehaviorProfileId.New(), UserId = domainId };

        var (window, recent) = UserProfileSerializer.Deserialize(entity.FeatureVector);

        // La feature del acceso ACTUAL se deriva del historial PREVIO (la petición llega sobre esa actividad).
        var features = _extractor.Extract(
            new RequestContext { SourceIp = string.Empty, Timestamp = update.Timestamp },
            recent);

        var newWindow = Append(window, features, _options.TrainingWindowMax);
        var newRecent = Append(recent, new UserAccessSample(update.Timestamp, update.Endpoint), _options.RecentAccessesMax);

        entity.AccessCount += 1;
        entity.IsColdStart = entity.AccessCount < update.ColdStartN;
        entity.BaseRiskPenalty = update.BaseRiskPenalty;
        entity.FeatureVector = UserProfileSerializer.Serialize(newWindow, newRecent);

        if (isNew)
            db.UserBehaviorProfiles.Add(entity);

        await db.SaveChangesAsync(cancellationToken);

        // Refrescar la caché caliente para que la próxima evaluación no pegue a Postgres.
        var profile = new UserAnomalyProfile(newWindow, newRecent, entity.AccessCount, entity.IsColdStart);
        await _redis.CacheProfileAsync(
            update.UserId,
            UserProfileSerializer.SerializeForCache(profile),
            TimeSpan.FromMinutes(_options.ProfileCacheTtlMinutes));
    }

    /// <summary>Agrega un elemento a una ventana rodante, conservando a lo sumo <paramref name="max"/> (los más recientes).</summary>
    private static IReadOnlyList<T> Append<T>(IReadOnlyList<T> source, T item, int max)
    {
        var list = new List<T>(source) { item };
        if (list.Count > max)
            list.RemoveRange(0, list.Count - max);
        return list;
    }
}
