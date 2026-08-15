namespace OG.Dashboard.Api.Contracts.AuditLogs;

/// <summary>Contrato basado en el SRS §4.1 — GET /api/v1/logs.</summary>
public sealed record AuditLogDto(
    Guid EvaluationId,
    DateTimeOffset Timestamp,

    /// <summary>Null si el usuario no pudo resolverse.</summary>
    string? UserId,
    string? Username,

    /// <summary>Null si el destino no era un upstream registrado.</summary>
    string? ServiceName,
    string SourceIp,
    GeoDto? Geo,
    string? UserAgent,
    decimal PolicyScore,
    decimal AnomalyScore,
    decimal RiskScore,
    string Verdict,
    IReadOnlyList<string> TriggeredRules
);

public sealed record GeoDto(
    string Country,
    string City,
    double? Latitude,
    double? Longitude
);
