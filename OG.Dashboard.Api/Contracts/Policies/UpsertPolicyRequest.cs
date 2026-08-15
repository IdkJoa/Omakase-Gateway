using System.ComponentModel.DataAnnotations;

namespace OG.Dashboard.Api.Contracts.Policies;

public sealed record UpsertPolicyRequest(
    [Required, MinLength(3), MaxLength(100)]
    string Name,

    /// <summary>Geofence | TimeWindow | Fingerprint | ImpossibleTravel.</summary>
    [Required]
    string Type,

    /// <summary>
    /// Claves snake_case que parsea el motor:
    /// Geofence: { "allowed_countries": [...] } y/o { "denied_countries": [...] }
    /// TimeWindow: { "start_time", "end_time", "timezone" }
    /// Fingerprint / ImpossibleTravel: sin parámetros ({}).
    /// </summary>
    [Required]
    object Config,

    [Range(0.0, 1.0)]
    decimal Weight,

    bool IsActive = true
);
