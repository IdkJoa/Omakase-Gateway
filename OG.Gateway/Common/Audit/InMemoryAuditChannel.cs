using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Application.Common.Audit;

/// <summary>
/// Implementación del canal de auditoría usando <see cref="System.Threading.Channels"/>.
/// Registrar como <b>Singleton</b>: el canal debe vivir toda la vida de la aplicación.
/// </summary>
/// <remarks>
/// <b>Capacidad</b>: 10 000 eventos (bounded). Si el canal se llena, <see cref="TryWrite"/>
/// devuelve <c>false</c> y el evento se descarta sin bloquear el pipeline HTTP.
/// La capacidad es configurable en <c>appsettings.json</c> cuando se implemente T-100.<br/>
/// <b>FullMode</b>: <see cref="BoundedChannelFullMode.DropWrite"/> — nunca bloquea al escritor.<br/>
/// <b>SingleReader</b>: <c>true</c> — solo el <c>AuditPersistenceWorker</c> (T-100) lee.<br/>
/// <b>SingleWriter</b>: <c>false</c> — múltiples peticiones concurrentes escriben.
/// </remarks>
public sealed class InMemoryAuditChannel : IAuditChannel
{
    private const int DefaultCapacity = 10_000;

    private readonly Channel<AuditEvent> _channel;
    private readonly ILogger<InMemoryAuditChannel> _logger;

    public InMemoryAuditChannel(ILogger<InMemoryAuditChannel> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<AuditEvent>(new BoundedChannelOptions(DefaultCapacity)
        {
            FullMode     = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    /// <inheritdoc/>
    public bool TryWrite(AuditEvent auditEvent)
    {
        var written = _channel.Writer.TryWrite(auditEvent);

        if (!written)
        {
            // El canal está lleno: el evento se descarta para no bloquear el pipeline.
            // T-100 puede añadir métricas (contador de drops) aquí cuando sea necesario.
            _logger.LogWarning(
                "[AuditChannel] Canal lleno — AuditEvent descartado. " +
                "EvaluationId={EvaluationId} Verdict={Verdict}",
                auditEvent.EvaluationId,
                auditEvent.Verdict);
        }

        return written;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<AuditEvent> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var auditEvent in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return auditEvent;
        }
    }
}
