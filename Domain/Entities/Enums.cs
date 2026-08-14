namespace Domain.Entities;

public enum UserType
{
    SecurityOfficer = 1,
    Client = 2
}

public enum PolicyType
{
    Geofence = 1,
    TimeWindow = 2,
    Fingerprint = 3,
    ImpossibleTravel = 4
}

public enum Verdict
{
    Allow = 1,
    Challenge = 2,
    Block = 3
}