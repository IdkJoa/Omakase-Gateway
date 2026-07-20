using System.ComponentModel.DataAnnotations;

namespace OG.Dashboard.Api.Contracts.Policies;

/// <summary>
/// Payload para crear o actualizar una política de acceso.
/// Usado en POST /api/v1/policies y PUT /api/v1/policies/{id}.
/// </summary>
public sealed record UpsertPolicyRequest(
    /// <summary>Nombre descriptivo de la política. Requerido.</summary>
    [Required, MinLength(3), MaxLength(100)]
    string Name,

    /// <summary>
    /// Tipo de regla. Valores válidos: Geofence | TimeWindow | Fingerprint | ImpossibleTravel.
    /// </summary>
    [Required]
    string Type,

    /// <summary>
    /// Parámetros JSONB del tipo de regla, con las claves snake_case que parsea el motor:
    /// Geofence: { "allowed_countries": ["DO","US"] } y/o { "denied_countries": ["JP"] }
    /// TimeWindow: { "start_time": "08:00", "end_time": "20:00", "timezone": "America/Santo_Domingo" }
    /// Fingerprint / ImpossibleTravel: sin parámetros ({}).
    /// </summary>
    [Required]
    object Config,

    /// <summary>Factor de ponderación en el Policy Score. Rango [0,1] (T-047).</summary>
    [Range(0.0, 1.0)]
    decimal Weight,

    /// <summary>Estado activo de la política. Default: true.</summary>
    bool IsActive = true
);
