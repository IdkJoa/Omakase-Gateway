namespace Infrastructure.KeyVault;

public interface ISecretProvider
{
    Task<string?> GetSecretAsync(string secretName, CancellationToken cancellationToken = default);
}
