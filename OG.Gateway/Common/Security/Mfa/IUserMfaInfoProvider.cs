namespace Application.Common.Security.Mfa;

// IsInteractive es false para service accounts: en ese caso Challenge escala a Block.
public sealed record UserMfaInfo(bool IsInteractive, bool MfaEnabled);

public interface IUserMfaInfoProvider
{
    Task<UserMfaInfo?> GetAsync(string userId, CancellationToken cancellationToken = default);
}
