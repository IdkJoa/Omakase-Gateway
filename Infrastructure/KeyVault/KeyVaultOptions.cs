namespace Infrastructure.KeyVault;

public sealed class KeyVaultOptions
{
    public const string SectionName = "KeyVault";

    // También se puede inyectar vía variable de entorno AZURE_KEYVAULT_URL.
    public string VaultUri { get; set; } = string.Empty;

    public string JwtSecretName { get; set; } = "jwt-secret-key";

    public bool Enabled { get; set; } = true;
}
