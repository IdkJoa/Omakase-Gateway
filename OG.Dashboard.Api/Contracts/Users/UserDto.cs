namespace OG.Dashboard.Api.Contracts.Users;

public sealed record UserDto(
    Guid Id,
    string Username,
    string UserType,
    bool IsActive,
    int FailedAttempts,
    DateTimeOffset? LockedUntil,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,

    /// <summary>Nombres de rol, no IDs.</summary>
    IReadOnlyList<string> Roles,
    UserBehaviorSummaryDto? BehaviorProfile
);

public sealed record UserBehaviorSummaryDto(
    /// <summary>Por debajo del umbral cold-start se aplica penalización.</summary>
    int AccessCount,
    DateTimeOffset? LastAccessAt,
    double AvgRequestsPerHour,
    int UniqueEndpointsCount
);
