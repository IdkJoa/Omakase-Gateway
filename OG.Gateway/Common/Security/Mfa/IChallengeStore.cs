namespace Application.Common.Security.Mfa;

// Persistido en Redis como challenge:{challengeId}. FingerprintHash liga el step-up al dispositivo
// que originó el desafío.
public sealed record ChallengeData(
    string UserId,
    string? ServiceName,
    string? FingerprintHash,
    string SourceIp,
    DateTimeOffset CreatedAt);

// El desafío es de uso único: se elimina al verificarse.
public interface IChallengeStore
{
    Task StoreAsync(Guid challengeId, ChallengeData data, TimeSpan ttl);

    Task<ChallengeData?> GetAsync(Guid challengeId);

    Task RemoveAsync(Guid challengeId);
}
