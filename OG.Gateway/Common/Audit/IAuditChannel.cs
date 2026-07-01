namespace Application.Common.Audit;

/// <summary>
/// Canal de auditoría asíncrono para eventos de evaluación de riesgo.
/// </summary>
/// <remarks>
/// Desacopla temporalmente el pipeline de peticiones HTTP (escritura, latencia crítica)
/// de la persistencia en PostgreSQL (lectura, latencia tolerante).
/// <para>
/// <b>Escritores</b>: <c>RiskEvaluationMiddleware</c> — uno por petición concurrente.<br/>
/// <b>Lector único</b>: <c>AuditPersistenceWorker</c> (T-100) — BackgroundService que drena el canal.
/// </para>
/// </remarks>
public interface IAuditChannel
{
    /// <summary>
    /// Encola un <see cref="AuditEvent"/> de forma no-bloqueante (fire-and-forget).
    /// </summary>
    /// <returns>
    /// <c>true</c> si el evento fue encolado correctamente;
    /// <c>false</c> si el canal está lleno (capacidad máxima alcanzada) y el evento fue descartado.
    /// </returns>
    bool TryWrite(AuditEvent auditEvent);

    /// <summary>
    /// Secuencia asíncrona para consumir eventos del canal.
    /// Bloqueante hasta que haya items disponibles o se cancele el token.
    /// Usado exclusivamente por el <c>AuditPersistenceWorker</c> de T-100.
    /// </summary>
    IAsyncEnumerable<AuditEvent> ReadAllAsync(CancellationToken cancellationToken = default);
}
