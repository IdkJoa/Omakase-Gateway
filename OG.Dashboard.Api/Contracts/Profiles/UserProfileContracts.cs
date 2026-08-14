namespace OG.Dashboard.Api.Contracts.Profiles;

/// <summary>HU-026 / T-054 — sobre <c>UserBehaviorProfile</c> más los últimos accesos de <c>audit_logs</c>.</summary>
public sealed record UserProfileDto(
    Guid UserId,
    string Username,
    int AccessCount,
    bool IsColdStart,
    decimal BaseRiskPenalty,
    DateTimeOffset? LastTrainedAt,

    /// <summary>Null en cold-start o sin perfil entrenado: el front debe mostrar el estado de arranque en frío, no datos vacíos.</summary>
    FeatureVectorDto? FeatureVector,

    /// <summary>Últimos 10 accesos, más reciente primero.</summary>
    IReadOnlyList<RecentAccessDto> RecentAccesses);

/// <summary>Componentes en [0,1]; <see cref="TypicalHour"/> es la hora habitual decodificada (0–24).</summary>
public sealed record FeatureVectorDto(
    float HourSin,
    float HourCos,
    float Frequency,
    float Diversity,
    double TypicalHour);

public sealed record RecentAccessDto(
    DateTimeOffset EvaluatedAt,
    string Verdict,
    decimal RiskScore,
    string SourceIp,
    string? Country,
    string? City);
