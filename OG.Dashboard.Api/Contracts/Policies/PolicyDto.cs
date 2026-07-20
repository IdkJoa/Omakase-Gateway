namespace OG.Dashboard.Api.Contracts.Policies;

/// <summary>
/// Representa una política de acceso tal como se expone en la API administrativa.
/// Corresponde a la entidad <c>AccessPolicy</c> del dominio.
/// </summary>
public sealed record PolicyDto(
    Guid Id,

    /// <summary>Nombre descriptivo de la política.</summary>
    string Name,

    /// <summary>
    /// Tipo de regla: Geofence | TimeWindow | Fingerprint | ImpossibleTravel.
    /// Determina cómo se interpreta el campo <see cref="Config"/>.
    /// </summary>
    string Type,

    /// <summary>
    /// Parámetros JSONB específicos del tipo de regla (claves snake_case, las que parsea el motor).
    /// Ej: { "allowed_countries": ["DO","US"] } para Geofence.
    /// </summary>
    object Config,

    /// <summary>Factor de ponderación de esta regla en el Policy Score. Rango [0,1] (T-047).</summary>
    decimal Weight,

    /// <summary>Indica si la política está activa (soft-delete).</summary>
    bool IsActive,

    /// <summary>ID del Security Officer que creó la política.</summary>
    Guid CreatedById,

    /// <summary>Username del Security Officer que creó la política.</summary>
    string CreatedByUsername,

    DateTimeOffset CreatedAt
);
