using Application.Common.Options;
using Azure;
using Infrastructure.KeyVault;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace OG.Application.UnitTests.KeyVault;

/// <summary>
/// Pruebas unitarias para KeyVaultStartupValidator (T-068 / HU-031).
/// Verifica la interrupción del arranque (Fail-Closed) ante fallos 401, 403, 404, timeouts o secretos inválidos.
/// </summary>
public class KeyVaultStartupValidatorTests
{
    private readonly ISecretProvider _secretProviderMock;
    private readonly IHostEnvironment _environmentMock;
    private readonly ILogger<KeyVaultStartupValidator> _loggerMock;
    private readonly JwtOptions _jwtOptions;
    private readonly KeyVaultOptions _kvOptions;

    public KeyVaultStartupValidatorTests()
    {
        // Desactivar Environment.Exit(1) durante las pruebas para capturar las excepciones
        Environment.SetEnvironmentVariable("DISABLE_KEYVAULT_EXIT_FOR_TESTS", "true");

        _secretProviderMock = Substitute.For<ISecretProvider>();
        _environmentMock = Substitute.For<IHostEnvironment>();
        _loggerMock = Substitute.For<ILogger<KeyVaultStartupValidator>>();

        _jwtOptions = new JwtOptions
        {
            SecretKey = "CHANGE_ME_IN_PRODUCTION_USE_AZURE_KEY_VAULT_MIN_32_BYTES!!"
        };

        _kvOptions = new KeyVaultOptions
        {
            VaultUri = "https://omakase-vault.vault.azure.net/",
            JwtSecretName = "jwt-secret-key",
            Enabled = true
        };
    }

    private KeyVaultStartupValidator CreateValidator()
    {
        return new KeyVaultStartupValidator(
            _secretProviderMock,
            Options.Create(_jwtOptions),
            Options.Create(_kvOptions),
            _environmentMock,
            _loggerMock);
    }

    [Fact]
    public async Task ValidateAsync_KeyVaultSecretIsValid_ShouldHydrateJwtSecretKey()
    {
        // Arrange
        var validKeyFromKeyVault = "valid-secret-key-from-azure-keyvault-must-be-at-least-32-bytes";
        _secretProviderMock.GetSecretAsync("jwt-secret-key", Arg.Any<CancellationToken>())
            .Returns(validKeyFromKeyVault);

        var validator = CreateValidator();

        // Act
        await validator.ValidateAndHydrateSecretsAsync();

        // Assert
        Assert.Equal(validKeyFromKeyVault, _jwtOptions.SecretKey);
    }

    [Fact]
    public async Task ValidateAsync_KeyVaultReturnsPlaceholderOrTooShortSecret_ShouldThrowException()
    {
        // Arrange: secreto demasiado corto o con placeholder
        _secretProviderMock.GetSecretAsync("jwt-secret-key", Arg.Any<CancellationToken>())
            .Returns("CHANGE_ME_TOO_SHORT");

        var validator = CreateValidator();

        // Act & Assert
        await Assert.ThrowsAsync<KeyVaultStartupException>(() => validator.ValidateAndHydrateSecretsAsync());
    }

    [Fact]
    public async Task ValidateAsync_KeyVaultThrowsRequestFailedException401_ShouldThrowException()
    {
        // Arrange: Simular error HTTP 401 Unauthorized de Azure SDK
        _secretProviderMock.GetSecretAsync("jwt-secret-key", Arg.Any<CancellationToken>())
            .Throws(new RequestFailedException(401, "Unauthorized access to Azure Key Vault"));

        var validator = CreateValidator();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<KeyVaultStartupException>(() => validator.ValidateAndHydrateSecretsAsync());
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task ValidateAsync_KeyVaultThrowsRequestFailedException404_ShouldThrowException()
    {
        // Arrange: Simular error HTTP 404 Secret Not Found de Azure SDK
        _secretProviderMock.GetSecretAsync("jwt-secret-key", Arg.Any<CancellationToken>())
            .Throws(new RequestFailedException(404, "Secret jwt-secret-key not found in vault"));

        var validator = CreateValidator();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<KeyVaultStartupException>(() => validator.ValidateAndHydrateSecretsAsync());
        Assert.Contains("404", ex.Message);
    }

    [Fact]
    public async Task ValidateAsync_KeyVaultTimesOut_ShouldThrowException()
    {
        // Arrange: Simular Timeout en consulta a Key Vault
        _secretProviderMock.GetSecretAsync("jwt-secret-key", Arg.Any<CancellationToken>())
            .Throws(new TimeoutException("Connection to Azure Key Vault timed out"));

        var validator = CreateValidator();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<KeyVaultStartupException>(() => validator.ValidateAndHydrateSecretsAsync());
        Assert.Contains("Timeout", ex.Message);
    }

    [Fact]
    public async Task ValidateAsync_DevelopmentEnvironmentWithoutVaultUri_ShouldSkipValidation()
    {
        // Arrange
        _kvOptions.VaultUri = string.Empty;
        Environment.SetEnvironmentVariable("AZURE_KEYVAULT_URL", string.Empty);
        _environmentMock.EnvironmentName.Returns("Development");

        var validator = CreateValidator();

        // Act
        await validator.ValidateAndHydrateSecretsAsync();

        // Assert: no intenta consultar el provider
        await _secretProviderMock.DidNotReceive().GetSecretAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
