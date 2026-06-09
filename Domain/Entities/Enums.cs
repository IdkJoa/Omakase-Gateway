namespace Domain.Entities;

public enum UserType
{
    SecurityOfficer = 1,
    Client = 2
}

/// <summary>
/// Discriminates the type of deterministic access policy rule.
/// </summary>
public enum PolicyType
{
    Geofence = 1,
    TimeWindow = 2,
    Fingerprint = 3,
    ImpossibleTravel = 4
}

/// <summary>
/// The verdict produced by the risk-score evaluation engine.
/// </summary>
public enum Verdict
{
    Allow = 1,
    Challenge = 2,
    Block = 3
}