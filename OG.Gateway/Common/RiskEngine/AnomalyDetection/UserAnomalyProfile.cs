namespace Application.Common.RiskEngine.AnomalyDetection;

// Se hidrata desde Redis (caliente) o PostgreSQL (frío), pero el motor no conoce esa procedencia.
public sealed record UserAnomalyProfile(
    IReadOnlyList<AnomalyFeatureVector> TrainingWindow,
    IReadOnlyList<UserAccessSample> RecentAccesses,
    int AccessCount,
    bool IsColdStart);
