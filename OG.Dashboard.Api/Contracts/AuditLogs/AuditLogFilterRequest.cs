namespace OG.Dashboard.Api.Contracts.AuditLogs;

/// <summary>
/// Parámetros de filtrado para GET /api/v1/logs.
/// Todos los parámetros son opcionales; la paginación tiene defaults seguros.
/// </summary>
public sealed record AuditLogFilterRequest(
    /// <summary>Número de página (1-indexed). Default: 1.</summary>
    int Page = 1,

    /// <summary>Registros por página. Default: 25. Máximo: 100.</summary>
    int PageSize = 25,

    /// <summary>Filtrar por veredicto: ALLOW | CHALLENGE | BLOCK.</summary>
    string? Verdict = null,

    /// <summary>Filtrar por ID de usuario.</summary>
    string? UserId = null,

    /// <summary>Límite inferior del rango temporal (ISO 8601).</summary>
    DateTimeOffset? From = null,

    /// <summary>Límite superior del rango temporal (ISO 8601).</summary>
    DateTimeOffset? To = null,

    /// <summary>Filtrar por IP de origen (coincidencia exacta).</summary>
    string? SourceIp = null,

    /// <summary>Filtrar por nombre de servicio protegido.</summary>
    string? ServiceName = null
);
