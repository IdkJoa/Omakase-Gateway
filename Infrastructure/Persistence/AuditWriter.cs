using Application.Common.RiskEngine;
using Domain.Entities;

namespace Infrastructure.Persistence;

/// <summary>
/// Persists audit records to PostgreSQL (T-030).
/// <para>
/// Direct write for now; this is the seam for the asynchronous fire-and-forget
/// channel (T-100, HU-037), which will replace this implementation behind
/// <see cref="IAuditWriter"/> without touching the scoring logic.
/// </para>
/// </summary>
public sealed class AuditWriter : IAuditWriter
{
    private readonly OmakaseDbContext _db;

    public AuditWriter(OmakaseDbContext db) => _db = db;

    public async Task WriteAsync(AuditLog auditLog, CancellationToken cancellationToken = default)
    {
        _db.AuditLogs.Add(auditLog);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
