using System.ComponentModel.DataAnnotations;

namespace OG.Dashboard.Api.Contracts.Services;

/// <summary>
/// Payload para crear o actualizar un servicio protegido.
/// Usado en POST /api/v1/services y PUT /api/v1/services/{id}.
/// </summary>
public sealed record UpsertServiceRequest(
    /// <summary>Nombre lógico único del servicio (usado como clusterId en YARP).</summary>
    [Required, MinLength(2), MaxLength(100)]
    string Name,

    /// <summary>URL completa del upstream de destino (ej. https://orders-svc:8080).</summary>
    [Required, Url]
    string UpstreamUrl,

    /// <summary>
    /// Si true, el Gateway exigirá un JWT válido antes de evaluar el Risk Score para este servicio.
    /// </summary>
    bool RequiresAuth,

    /// <summary>Estado activo del servicio. Default: true.</summary>
    bool IsActive = true
);
