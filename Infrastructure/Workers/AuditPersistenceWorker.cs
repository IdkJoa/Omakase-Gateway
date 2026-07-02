using Application.Common.Audit;
using Domain.Entities;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Workers;

/// <summary>
/// Worker asíncrono que consume <see cref="IAuditChannel"/> y persiste los eventos
/// en <c>audit_logs</c> (PostgreSQL) mediante el patrón batch-drain (T-100 / HU-037).
/// </summary>
/// <remarks>
/// <b>Patrón de operación:</b>
/// <list type="number">
///   <item><see cref="IAuditChannel.WaitToReadAsync"/> suspende el hilo hasta que haya eventos (sin busy-wait).</item>
///   <item><see cref="IAuditChannel.TryRead"/> drena hasta <see cref="AuditWorkerOptions.BatchSize"/> eventos sin bloquear.</item>
///   <item><see cref="PersistBatchAsync"/> crea un scope DI, resuelve <see cref="OmakaseDbContext"/> y hace AddRange + SaveChangesAsync.</item>
///   <item>En error de DB: re-encola el batch al canal y espera <see cref="AuditWorkerOptions.RetryDelaySeconds"/> antes de reintentar.</item>
///   <item>En apagado limpio: flush final del lote en curso antes de detenerse.</item>
/// </list>
/// <b>Por qué IServiceScopeFactory:</b> el worker es un Singleton (vive toda la app),
/// pero <see cref="OmakaseDbContext"/> es Scoped. Se crea un scope por batch para respetar
/// los ciclos de vida del contenedor DI.
/// </remarks>
public sealed class AuditPersistenceWorker : BackgroundService
{
    private readonly IAuditChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditPersistenceWorker> _logger;
    private readonly int _batchSize;
    private readonly TimeSpan _retryDelay;

    public AuditPersistenceWorker(
        IAuditChannel channel,
        IServiceScopeFactory scopeFactory,
        IOptions<AuditWorkerOptions> options,
        ILogger<AuditPersistenceWorker> logger)
    {
        _channel     = channel;
        _scopeFactory = scopeFactory;
        _logger      = logger;
        _batchSize   = options.Value.BatchSize;
        _retryDelay  = TimeSpan.FromSeconds(options.Value.RetryDelaySeconds);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "[AuditPersistenceWorker] Iniciado. BatchSize={BatchSize} RetryDelay={RetryDelay}s.",
            _batchSize, _retryDelay.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1. Esperar al menos un evento sin consumir CPU (no busy-wait).
                if (!await _channel.WaitToReadAsync(stoppingToken))
                    break; // Canal cerrado — apagado limpio.

                // 2. Drenar hasta _batchSize eventos de forma no-bloqueante.
                var batch = DrainBatch();

                if (batch.Count > 0)
                    await PersistBatchAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // Señal de apagado — salir del loop sin loguear como error.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[AuditPersistenceWorker] Error inesperado en el loop principal. Reintentando en {Delay}s.",
                    _retryDelay.TotalSeconds);

                await Task.Delay(_retryDelay, stoppingToken);
            }
        }

        _logger.LogInformation("[AuditPersistenceWorker] Detenido.");
    }

    /// <summary>
    /// Drena hasta <see cref="_batchSize"/> eventos del canal de forma no-bloqueante.
    /// </summary>
    private List<AuditEvent> DrainBatch()
    {
        var batch = new List<AuditEvent>(_batchSize);
        while (batch.Count < _batchSize && _channel.TryRead(out var ev))
            batch.Add(ev);
        return batch;
    }

    /// <summary>
    /// Persiste un lote de eventos en <c>audit_logs</c>.
    /// En caso de fallo, re-encola los eventos al canal para no perderlos.
    /// </summary>
    private async Task PersistBatchAsync(List<AuditEvent> batch, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OmakaseDbContext>();

            foreach (var ev in batch)
                db.AuditLogs.Add(MapToEntity(ev));

            await db.SaveChangesAsync(stoppingToken);

            _logger.LogInformation(
                "[AuditPersistenceWorker] {Count} evento(s) persistidos en audit_logs.",
                batch.Count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Apagado durante la escritura: re-encolar para no perder eventos.
            RequeueBatch(batch);
            throw;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex,
                "[AuditPersistenceWorker] Fallo de escritura en audit_logs ({Count} eventos). " +
                "Re-encolando al canal y esperando {Delay}s.",
                batch.Count, _retryDelay.TotalSeconds);

            RequeueBatch(batch);
            await Task.Delay(_retryDelay, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AuditPersistenceWorker] Error inesperado al persistir batch ({Count} eventos). " +
                "Re-encolando al canal y esperando {Delay}s.",
                batch.Count, _retryDelay.TotalSeconds);

            RequeueBatch(batch);
            await Task.Delay(_retryDelay, stoppingToken);
        }
    }

    /// <summary>
    /// Intenta re-encolar cada evento del batch fallido al canal.
    /// Los eventos que no quepan (canal lleno) se descartan con log de advertencia.
    /// </summary>
    private void RequeueBatch(List<AuditEvent> batch)
    {
        var requeued  = 0;
        var discarded = 0;

        foreach (var ev in batch)
        {
            if (_channel.TryWrite(ev)) requeued++;
            else                       discarded++;
        }

        _logger.LogWarning(
            "[AuditPersistenceWorker] Re-encolado: {Requeued} eventos OK, {Discarded} descartados (canal lleno).",
            requeued, discarded);
    }

    /// <summary>
    /// Mapea un <see cref="AuditEvent"/> a la entidad <see cref="AuditLog"/> de dominio.
    /// </summary>
    private static AuditLog MapToEntity(AuditEvent ev) => new()
    {
        Id           = AuditLogId.New(),       // UUID v7 — ordenado por tiempo para B-tree eficiente
        EvaluationId = ev.EvaluationId,
        SourceIp     = ev.SourceIp,
        UserAgent    = ev.UserAgent,
        // UserId del JWT claim "sub" es un GUID formateado como string.
        // Nulo si el actor no está autenticado (HU-auth no implementada aún).
        UserId       = Guid.TryParse(ev.UserId, out var uid)
                           ? UserId.From(uid)
                           : null,
        Verdict      = ev.Verdict,
        RiskScore    = ev.RiskScore,
        PolicyScore  = ev.PolicyScore,
        AnomalyScore = ev.AnomalyScore,
        EvaluatedAt  = ev.EvaluatedAt,
        // T-030 (HU-015): completados por el motor de riesgo real.
        Geo             = ev.Geo,
        TriggeredRules  = ev.TriggeredRules,
        FingerprintHash = ev.FingerprintHash,
        ServiceId       = ev.ServiceId is Guid sid ? ProtectedServiceId.From(sid) : null,
    };
}
