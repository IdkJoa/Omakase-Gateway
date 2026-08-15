namespace Infrastructure.KeyVault;

public sealed class KeyVaultStartupException : Exception
{
    public KeyVaultStartupException(string message) : base(message) { }
    public KeyVaultStartupException(string message, Exception innerException) : base(message, innerException) { }
}
