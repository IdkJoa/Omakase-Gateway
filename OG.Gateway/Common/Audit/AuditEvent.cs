using System.Text.Json;
using Domain.Entities;

namespace Application.Common.Audit;

/// <summary>
/// DTO inmutable que representa una evaluación de riesgo completada,
/// lista para ser persistida en <c>audit_logs</c>.
/// </summary>
/// <remarks>
/// Viaja por el <see cref="IAuditChannel"/> desde el <c>RiskEvaluationMiddleware</c>
/// hasta el <c>BackgroundService</c> de T-100 que lo persiste en PostgreSQL.
/// Los campos <see cref="PolicyScore"/> y <see cref="AnomalyScore"/> están presentes
/// en el contrato desde T-016 con valor <c>0m</c>; T-016+ los rellenará con valores reales
/// sin cambiar la firma del record.
/// </remarks>
/// <param name="EvaluationId">Identificador único de la evaluación. Expuesto en la respuesta HTTP para correlación cliente-gateway.</param>
/// <param name="SourceIp">IP de origen de la petición (ya procesada por <c>UseForwardedHeaders</c>).</param>
/// <param name="UserAgent">User-Agent sanitizado. Null si no se proporcionó.</param>
/// <param name="UserId">Identificador del actor autenticado. Null hasta que se implemente HU-auth.</param>
/// <param name="Verdict">Veredicto emitido por el motor de riesgo.</param>
/// <param name="RiskScore">Score de riesgo consolidado (0–100).</param>
/// <param name="PolicyScore">Score de la capa determinista (0–100). <c>0m</c> en T-016.</param>
/// <param name="AnomalyScore">Score de la capa de anomalías ML (0–100). <c>0m</c> en T-016.</param>
/// <param name="TraceId">Identificador de traza del <c>HttpContext</c> para correlación con OTel.</param>
/// <param name="EvaluatedAt">Instante UTC en que el veredicto fue emitido.</param>
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
