namespace OG.Dashboard.Api.Contracts.Users;

/// <summary>
/// Representa un usuario del sistema tal como se expone en la API administrativa.
/// Incluye datos de identidad, estado de bloqueo, roles asignados y resumen de comportamiento.
/// </summary>
public sealed record UserDto(
    Guid Id,
    string Username,

    /// <summary>Tipo de actor: CLIENT_USER | SECURITY_OFFICER.</summary>
    string UserType,

    bool IsActive,

    /// <summary>Número de intentos de autenticación fallidos consecutivos.</summary>
    int FailedAttempts,

    /// <summary>Timestamp hasta el que la cuenta está bloqueada; null si no está bloqueada.</summary>
    DateTimeOffset? LockedUntil,

    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,

    /// <summary>Roles asignados al usuario (nombres, no IDs).</summary>
    IReadOnlyList<string> Roles,

    /// <summary>Resumen del perfil de comportamiento; null si aún no existe.</summary>
    UserBehaviorSummaryDto? BehaviorProfile
);

/// <summary>
/// Resumen del perfil de comportamiento del usuario para la vista de listado.
/// </summary>
public sealed record UserBehaviorSummaryDto(
    /// <summary>Total de accesos registrados. Cuando &lt; N (cold-start threshold) se aplica penalización.</summary>
    int AccessCount,

    DateTimeOffset? LastAccessAt,

    /// <summary>Promedio de peticiones por hora en la ventana de observación.</summary>
    double AvgRequestsPerHour,

    /// <summary>Diversidad de endpoints accedidos (número de rutas únicas).</summary>
    int UniqueEndpointsCount
);
