namespace OG.Dashboard.Api.Contracts.Policies;

/// <summary>HU-023 T-048 — POST /api/v1/services/{serviceId}/policies.</summary>
public sealed record AssociatePolicyRequest(
    Guid PolicyId,
    bool IsEnabled = true
);

/// <summary>Enriquecida con los datos de la política para que el front no necesite una segunda llamada.</summary>
public sealed record ServicePolicyDto(
    Guid Id,
    Guid PolicyId,
    string PolicyName,
    string PolicyType,
    decimal Weight,
    bool IsEnabled,

    /// <summary>El motor solo evalúa asociaciones con IsEnabled=true Y política con IsActive=true.</summary>
    bool PolicyIsActive
);
