namespace Infrastructure.KeyVault;

/// <summary>
/// Proveedor de secretos para consultar Azure Key Vault de forma segura (T-068 / HU-031).
/// </summary>
public interface ISecretProvider
{
    Task<string?> GetSecretAsync(string secretName, CancellationToken cancellationToken = default);
}
