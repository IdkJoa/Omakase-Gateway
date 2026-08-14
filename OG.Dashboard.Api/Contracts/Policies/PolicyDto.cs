namespace OG.Dashboard.Api.Contracts.Policies;

public sealed record PolicyDto(
    Guid Id,
    string Name,

    /// <summary>Determina cómo se interpreta <see cref="Config"/>: Geofence | TimeWindow | Fingerprint | ImpossibleTravel.</summary>
    string Type,

    /// <summary>Claves snake_case que parsea el motor, ej. { "allowed_countries": ["DO","US"] } para Geofence.</summary>
    object Config,
    decimal Weight,
    bool IsActive,
    Guid CreatedById,
    string CreatedByUsername,
    DateTimeOffset CreatedAt
);
