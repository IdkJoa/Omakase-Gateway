namespace OG.Dashboard.Api.Contracts.AuditLogs;

/// <summary>
/// Representa una entrada del log de auditoría tal como se expone en la API.
/// Contrato basado en el SRS §4.1 — GET /api/v1/logs.
/// </summary>
public sealed record AuditLogDto(
    /// <summary>Identificador estable de la evaluación expuesto en la superficie de la API.</summary>
    Guid EvaluationId,

    /// <summary>Timestamp UTC de la evaluación.</summary>
    DateTimeOffset Timestamp,

    /// <summary>Identificador del usuario que originó la petición; null si no pudo resolverse.</summary>
    string? UserId,

    /// <summary>Username del usuario; null si no pudo resolverse.</summary>
    string? Username,

    /// <summary>Servicio protegido destino; null si no era un upstream registrado.</summary>
    string? ServiceName,

    /// <summary>Dirección IP de origen (IPv4 o IPv6).</summary>
    string SourceIp,

    /// <summary>Resolución geográfica de la IP de origen.</summary>
    GeoDto? Geo,

    /// <summary>User-Agent sanitizado.</summary>
    string? UserAgent,

    /// <summary>Score producido por la capa de políticas deterministas (0–100).</summary>
    decimal PolicyScore,

    /// <summary>Score producido por la capa de detección de anomalías IA (0–100).</summary>
    decimal AnomalyScore,

    /// <summary>Risk Score consolidado final (0–100).</summary>
    decimal RiskScore,

    /// <summary>Veredicto del motor de evaluación: ALLOW | CHALLENGE | BLOCK.</summary>
    string Verdict,

    /// <summary>Reglas de política disparadas en esta evaluación.</summary>
    IReadOnlyList<string> TriggeredRules
);

/// <summary>Resolución geográfica de una dirección IP.</summary>
public sealed record GeoDto(
    string Country,
    string City,
    double? Latitude,
    double? Longitude
);
