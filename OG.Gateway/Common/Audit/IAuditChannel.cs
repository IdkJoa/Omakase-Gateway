namespace Application.Common.Audit;

// Desacopla el pipeline HTTP (latencia crítica) de la persistencia en PostgreSQL (latencia tolerante).
// Escritores: uno por petición concurrente. Lector único: AuditPersistenceWorker (T-100).
public interface IAuditChannel
{
    bool TryWrite(AuditEvent auditEvent);

    IAsyncEnumerable<AuditEvent> ReadAllAsync(CancellationToken cancellationToken = default);

    // Suspende sin busy-waiting; usado por el worker para iniciar cada ciclo de batch-drain.
    ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default);

    bool TryRead(out AuditEvent auditEvent);
}
