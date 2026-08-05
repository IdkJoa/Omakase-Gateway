namespace Infrastructure.KeyVault;

/// <summary>
/// Excepción lanzada cuando Azure Key Vault falla durante el inicio del Gateway (T-068 / HU-031).
/// </summary>
public sealed class KeyVaultStartupException : Exception
{
    public KeyVaultStartupException(string message) : base(message) { }
    public KeyVaultStartupException(string message, Exception innerException) : base(message, innerException) { }
}
