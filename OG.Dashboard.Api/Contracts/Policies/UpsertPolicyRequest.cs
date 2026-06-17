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
    /// Parámetros JSONB del tipo de regla.
    /// Ej para Geofence: { "allowedCountries": ["DO", "US"] }
    /// Ej para TimeWindow: { "startHour": 8, "endHour": 20, "daysOfWeek": [1,2,3,4,5] }
    /// Ej para ImpossibleTravel: { "maxSpeedKmh": 900 }
    /// </summary>
    [Required]
    object Config,

    /// <summary>Factor de ponderación en el Policy Score (0.001–9.999).</summary>
    [Range(0.001, 9.999)]
    decimal Weight,

    /// <summary>Estado activo de la política. Default: true.</summary>
    bool IsActive = true
);
