namespace OG.Dashboard.Api.Contracts.Common;

/// <summary>Contrato uniforme para todos los errores (400–500) del Dashboard API.</summary>
public sealed record ErrorResponse(
    string ErrorCode,
    string Message,

    /// <summary>TraceId de OpenTelemetry para correlacionar con los logs del sistema.</summary>
    string TraceId
);
