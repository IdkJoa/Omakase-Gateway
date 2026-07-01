using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Common.Audit;

/// <summary>
/// Implementación del canal de auditoría usando <see cref="System.Threading.Channels"/>.
/// Registrar como <b>Singleton</b>: el canal debe vivir toda la vida de la aplicación.
/// </summary>
/// <remarks>
/// La capacidad se configura vía <see cref="AuditChannelOptions.Capacity"/> en <c>appsettings.json</c>.<br/>
/// <b>FullMode</b>: <see cref="BoundedChannelFullMode.DropWrite"/> — nunca bloquea al escritor.<br/>
/// <b>SingleReader</b>: <c>true</c> — solo el <c>AuditPersistenceWorker</c> lee.<br/>
/// <b>SingleWriter</b>: <c>false</c> — múltiples peticiones concurrentes escriben.
/// </remarks>
public sealed class InMemoryAuditChannel : IAuditChannel
{
    private readonly Channel<AuditEvent> _channel;
    private readonly ILogger<InMemoryAuditChannel> _logger;

    public InMemoryAuditChannel(
        IOptions<AuditChannelOptions> options,
        ILogger<InMemoryAuditChannel> logger)
    {
        _logger = logger;

        var capacity = options.Value.Capacity;

        _channel = Channel.CreateBounded<AuditEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode                      = BoundedChannelFullMode.DropWrite,
            SingleReader                  = true,
            SingleWriter                  = false,
            AllowSynchronousContinuations = false
        });

        _logger.LogInformation(
            "[AuditChannel] Inicializado con capacidad={Capacity}.", capacity);
    }

    /// <inheritdoc/>
    public bool TryWrite(AuditEvent auditEvent)
    {
        var written = _channel.Writer.TryWrite(auditEvent);

        if (!written)
        {
            _logger.LogWarning(
                "[AuditChannel] Canal lleno — AuditEvent descartado. " +
                "EvaluationId={EvaluationId} Verdict={Verdict}",
                auditEvent.EvaluationId,
                auditEvent.Verdict);
        }

        return written;
    }
    
    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        => _channel.Reader.WaitToReadAsync(cancellationToken);

    public bool TryRead(out AuditEvent auditEvent)
        => _channel.Reader.TryRead(out auditEvent!);
    
    public async IAsyncEnumerable<AuditEvent> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var auditEvent in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return auditEvent;
        }
    }
}
