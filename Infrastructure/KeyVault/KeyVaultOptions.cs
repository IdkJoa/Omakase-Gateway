namespace Infrastructure.KeyVault;

/// <summary>
/// Opciones de configuración para Azure Key Vault (T-068 / HU-031).
/// </summary>
public sealed class KeyVaultOptions
{
    public const string SectionName = "KeyVault";

    /// <summary>
    /// URI de Azure Key Vault (ej. "https://omakase-vault.vault.azure.net/").
    /// También se puede inyectar vía variable de entorno AZURE_KEYVAULT_URL.
    /// </summary>
    public string VaultUri { get; set; } = string.Empty;

    /// <summary>
    /// Nombre del secreto en Key Vault que almacena la clave de firma JWT (ej. "jwt-secret-key").
    /// </summary>
    public string JwtSecretName { get; set; } = "jwt-secret-key";

    /// <summary>
    /// Indica si Key Vault es requerido en el entorno actual.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
