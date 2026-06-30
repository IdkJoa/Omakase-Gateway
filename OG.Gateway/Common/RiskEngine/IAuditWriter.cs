using Domain.Entities;

namespace Application.Common.RiskEngine;

/// <summary>
/// Persists the immutable audit record of an evaluation (T-030).
/// <para>
/// This is the seam for the asynchronous fire-and-forget audit channel (T-100,
/// HU-037): the current implementation writes directly, and HU-037 can replace
/// it behind this interface without touching the scoring logic.
/// </para>
/// </summary>
public interface IAuditWriter
{
    Task WriteAsync(AuditLog auditLog, CancellationToken cancellationToken = default);
}
