using System.Text.Json;
using Domain.Entities;

namespace Application.Common.Audit;

// PolicyScore y AnomalyScore están en el contrato desde T-016 con valor 0m; se rellenan con
// valores reales en T-016+ sin cambiar la firma del record, para no romper consumidores existentes.
public sealed record AuditEvent(
    Guid            EvaluationId,
    string          SourceIp,
    string?         UserAgent,
    string?         UserId,
    Verdict         Verdict,
    decimal         RiskScore,
    decimal         PolicyScore,
    decimal         AnomalyScore,
    string          TraceId,
    DateTimeOffset  EvaluatedAt,
    JsonDocument?   Geo = null,
    JsonDocument?   TriggeredRules = null,
    Guid?           ServiceId = null,
    string?         FingerprintHash = null);
