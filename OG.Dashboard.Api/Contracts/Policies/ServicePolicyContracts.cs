namespace OG.Dashboard.Api.Contracts.Policies;

/// <summary>
/// Payload para asociar una política a un servicio protegido (HU-023 T-048).
/// Usado en POST /api/v1/services/{serviceId}/policies.
/// </summary>
public sealed record AssociatePolicyRequest(
    /// <summary>ID de la política de acceso a asociar. Requerido.</summary>
    Guid PolicyId,

    /// <summary>
    /// Permite activar/desactivar la política para este servicio concreto
    /// sin eliminar la asociación. Default: true.
    /// </summary>
    bool IsEnabled = true
);

/// <summary>
/// Representa una asociación servicio↔política tal como se expone en la API
/// administrativa. Corresponde a la entidad <c>ServicePolicy</c> del dominio,
/// enriquecida con los datos de la política para que el front no tenga que
/// resolverlos con una segunda llamada.
/// </summary>
public sealed record ServicePolicyDto(
    /// <summary>ID de la asociación (fila de service_policies).</summary>
    Guid Id,

    /// <summary>ID de la política asociada.</summary>
    Guid PolicyId,

    /// <summary>Nombre descriptivo de la política.</summary>
    string PolicyName,

    /// <summary>Tipo de regla: Geofence | TimeWindow | Fingerprint | ImpossibleTravel.</summary>
    string PolicyType,

    /// <summary>Factor de ponderación de la política en el Policy Score.</summary>
    decimal Weight,

    /// <summary>Si la asociación está habilitada para este servicio.</summary>
    bool IsEnabled,

    /// <summary>
    /// Si la política en sí está activa (soft-delete global). El motor solo evalúa
    /// asociaciones con IsEnabled=true Y política con IsActive=true.
    /// </summary>
    bool PolicyIsActive
);
