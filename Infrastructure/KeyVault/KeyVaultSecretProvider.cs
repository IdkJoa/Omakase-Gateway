using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.KeyVault;

/// <summary>
/// Cliente de Azure Key Vault para consultar secretos con DefaultAzureCredential (T-068 / HU-031).
/// </summary>
public class KeyVaultSecretProvider : ISecretProvider
{
    private readonly SecretClient? _secretClient;
    private readonly ILogger<KeyVaultSecretProvider> _logger;

    public KeyVaultSecretProvider(IOptions<KeyVaultOptions> options, ILogger<KeyVaultSecretProvider> logger)
    {
        _logger = logger;

        var vaultUriRaw = options.Value.VaultUri;
        if (string.IsNullOrWhiteSpace(vaultUriRaw))
        {
            vaultUriRaw = Environment.GetEnvironmentVariable("AZURE_KEYVAULT_URL");
        }

        if (!string.IsNullOrWhiteSpace(vaultUriRaw) && Uri.TryCreate(vaultUriRaw, UriKind.Absolute, out var vaultUri))
        {
            _secretClient = new SecretClient(vaultUri, new DefaultAzureCredential());
        }
        else
        {
            _logger.LogWarning("[KeyVault] URL de Azure Key Vault no configurada o no válida.");
        }
    }

    // Constructor para inyección directa o pruebas unitarias con SecretClient mock/personalizado
    public KeyVaultSecretProvider(SecretClient secretClient, ILogger<KeyVaultSecretProvider> logger)
    {
        _secretClient = secretClient;
        _logger = logger;
    }

    public virtual async Task<string?> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        if (_secretClient is null)
        {
            throw new InvalidOperationException("El cliente de Azure Key Vault no está inicializado (falta AZURE_KEYVAULT_URL).");
        }

        if (string.IsNullOrWhiteSpace(secretName))
        {
            throw new ArgumentException("El nombre del secreto no puede estar vacío.", nameof(secretName));
        }

        try
        {
            _logger.LogInformation("[KeyVault] Intentando consultar el secreto '{SecretName}' desde Azure Key Vault...", secretName);

            KeyVaultSecret secret = await _secretClient.GetSecretAsync(secretName, cancellationToken: cancellationToken);
            return secret.Value;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, 
                "[KeyVault] Fallo al consultar el secreto '{SecretName}'. Código HTTP: {Status}, Mensaje: {Message}",
                secretName, ex.Status, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KeyVault] Error inesperado o timeout al consultar Azure Key Vault para '{SecretName}'.", secretName);
            throw;
        }
    }
}
