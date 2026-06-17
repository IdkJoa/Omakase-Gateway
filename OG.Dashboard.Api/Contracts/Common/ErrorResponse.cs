namespace OG.Dashboard.Api.Contracts.Common;

/// <summary>
/// Contrato uniforme de error para todos los endpoints del Dashboard API.
/// Todos los errores (400, 401, 403, 404, 409, 429, 500) usan esta estructura.
/// </summary>
public sealed record ErrorResponse(
    /// <summary>Código de error semántico. Ej: VALIDATION_ERROR, NOT_FOUND, UNAUTHORIZED.</summary>
    string ErrorCode,

    /// <summary>Mensaje legible por el desarrollador frontend.</summary>
    string Message,

    /// <summary>TraceId de OpenTelemetry para correlacionar con los logs del sistema.</summary>
    string TraceId
);
