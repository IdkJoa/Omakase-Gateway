namespace OG.Dashboard.Api.Contracts.Profiles;

/// <summary>
/// Perfil de comportamiento aprendido de un usuario (HU-026 / T-054), sobre la entidad
/// <c>UserBehaviorProfile</c> más los últimos accesos de <c>audit_logs</c>.
/// </summary>
public sealed record UserProfileDto(
    Guid UserId,
    string Username,

    /// <summary>Total de accesos evaluados acumulados para el usuario.</summary>
    int AccessCount,

    /// <summary>True mientras el usuario no tiene historial suficiente (arranque en frío).</summary>
    bool IsColdStart,

    /// <summary>Penalización de riesgo vigente por cold-start (decrece con AccessCount).</summary>
    decimal BaseRiskPenalty,

    /// <summary>Último reentrenamiento del modelo para el usuario; null si nunca se entrenó.</summary>
    DateTimeOffset? LastTrainedAt,

    /// <summary>
    /// Vector de características decodificado (representativo). <c>null</c> en cold-start o sin
    /// perfil entrenado: en ese caso el front muestra el estado de arranque en frío, no datos vacíos.
    /// </summary>
    FeatureVectorDto? FeatureVector,

    /// <summary>Últimos 10 accesos del usuario en audit_logs (más reciente primero).</summary>
    IReadOnlyList<RecentAccessDto> RecentAccesses);

/// <summary>
/// Feature vector decodificado y promediado a un centroide representativo. Las cuatro componentes
/// están en [0,1]; <see cref="TypicalHour"/> es la hora habitual decodificada (0–24).
/// </summary>
public sealed record FeatureVectorDto(
    float HourSin,
    float HourCos,
    float Frequency,
    float Diversity,
    double TypicalHour);

/// <summary>Un acceso reciente del usuario, tomado de audit_logs.</summary>
public sealed record RecentAccessDto(
    DateTimeOffset EvaluatedAt,
    string Verdict,
    decimal RiskScore,
    string SourceIp,
    string? Country,
    string? City);
