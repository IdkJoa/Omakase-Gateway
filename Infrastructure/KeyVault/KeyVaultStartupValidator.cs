using System.Text;
using Application.Common.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.KeyVault;

/// <summary>
/// Validador de inicio que exige la disponibilidad de Azure Key Vault para obtener los secretos críticos (T-068 / HU-031).
/// Si falla la lectura por timeout, 401, 403, 404 o red, INTERRUMPE el arranque inmediatamente con código != 0.
/// </summary>
public sealed class KeyVaultStartupValidator
{
    private readonly ISecretProvider _secretProvider;
    private readonly JwtOptions _jwtOptions;
    private readonly KeyVaultOptions _kvOptions;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<KeyVaultStartupValidator> _logger;

    public KeyVaultStartupValidator(
        ISecretProvider secretProvider,
        IOptions<JwtOptions> jwtOptions,
        IOptions<KeyVaultOptions> kvOptions,
        IHostEnvironment environment,
        ILogger<KeyVaultStartupValidator> logger)
    {
        _secretProvider = secretProvider;
        _jwtOptions = jwtOptions.Value;
        _kvOptions = kvOptions.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task ValidateAndHydrateSecretsAsync(CancellationToken cancellationToken = default)
    {
        var vaultUri = _kvOptions.VaultUri;
        if (string.IsNullOrWhiteSpace(vaultUri))
        {
            vaultUri = Environment.GetEnvironmentVariable("AZURE_KEYVAULT_URL");
        }

        bool isKeyVaultConfigured = !string.IsNullOrWhiteSpace(vaultUri);
        bool isProduction = _environment.IsProduction();

        // Si se configuró explícitamente AZURE_KEYVAULT_URL o VaultUri, la validación de Key Vault es OBLIGATORIA
        if (isKeyVaultConfigured)
        {
            _logger.LogInformation("[KeyVault] Iniciando validación estricta de Azure Key Vault en startup (HU-031 / T-068)...");

            try
            {
                var secretName = string.IsNullOrWhiteSpace(_kvOptions.JwtSecretName) ? "jwt-secret-key" : _kvOptions.JwtSecretName;
                string? secretValue = await _secretProvider.GetSecretAsync(secretName, cancellationToken);

                if (string.IsNullOrWhiteSpace(secretValue) || secretValue.Contains("CHANGE_ME") || Encoding.UTF8.GetByteCount(secretValue) < 32)
                {
                    var msg = $"El secreto '{secretName}' en Azure Key Vault es nulo o inválido (<32 bytes o contiene placeholder).";
                    FailStartupAndExit(msg);
                }

                // Inyectar el secreto oficial obtenido de Key Vault en JwtOptions
                _jwtOptions.SecretKey = secretValue!;
                _logger.LogInformation("[KeyVault] ÉXITO: Secreto JWT obtenido e inyectado desde Azure Key Vault en el arranque.");
            }
            catch (Exception ex)
            {
                var failureMsg = $"Fallo crítico al consultar Azure Key Vault durante el arranque. Timeout, 401/403 (Autenticación), 404 (Secreto no existe) o error de red: {ex.Message}";
                FailStartupAndExit(failureMsg, ex);
            }
        }
        else
        {
            _logger.LogWarning("[KeyVault] Modo Desarrollo sin AZURE_KEYVAULT_URL. Usando clave provisional de appsettings (T-102).");
        }
    }

    private void FailStartupAndExit(string reason, Exception? innerException = null)
    {
        _logger.LogCritical(innerException, 
            "[CRITICAL_STARTUP_FAILURE] AZURE KEY VAULT INDISPONIBLE. El Gateway NO PUEDE ARRANCAR sin secretos verificados (Fail-Closed Zero Trust HU-031 / T-068). Razón: {Reason}", 
            reason);

        var startupException = new KeyVaultStartupException(reason, innerException!);
        
        // Si no estamos en entorno de prueba automatizada, forzamos salida con código 1
        if (Environment.GetEnvironmentVariable("DISABLE_KEYVAULT_EXIT_FOR_TESTS") != "true")
        {
            Environment.Exit(1);
        }

        throw startupException;
    }
}
