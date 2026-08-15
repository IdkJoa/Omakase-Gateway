namespace OG.Dashboard.Api.Contracts.Services;

public sealed record ProtectedServiceDto(
    Guid Id,

    /// <summary>Usado como clusterId en YARP.</summary>
    string Name,
    string UpstreamUrl,
    bool RequiresAuth,
    bool IsActive,
    DateTimeOffset CreatedAt,
    int AssociatedPoliciesCount
);
