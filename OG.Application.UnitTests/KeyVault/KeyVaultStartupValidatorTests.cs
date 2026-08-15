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

// KeyVaultStartupValidator (T-068 / HU-031): interrumpe el arranque (fail-closed) ante fallos 401, 403, 404, timeouts o secretos inválidos.
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
        var validKeyFromKeyVault = "valid-secret-key-from-azure-keyvault-must-be-at-least-32-bytes";
        _secretProviderMock.GetSecretAsync("jwt-secret-key", Arg.Any<CancellationToken>())
            .Returns(validKeyFromKeyVault);

        var validator = CreateValidator();

        await validator.ValidateAndHydrateSecretsAsync();

        Assert.Equal(validKeyFromKeyVault, _jwtOptions.SecretKey);
    }

    [Fact]
    public async Task ValidateAsync_KeyVaultReturnsPlaceholderOrTooShortSecret_ShouldThrowException()
    {
        _secretProviderMock.GetSecretAsync("jwt-secret-key", Arg.Any<CancellationToken>())
            .Returns("CHANGE_ME_TOO_SHORT");

        var validator = CreateValidator();

        await Assert.ThrowsAsync<KeyVaultStartupException>(() => validator.ValidateAndHydrateSecretsAsync());
    }

    [Fact]
    public async Task ValidateAsync_KeyVaultThrowsRequestFailedException401_ShouldThrowException()
    {
        _secretProviderMock.GetSecretAsync("jwt-secret-key", Arg.Any<CancellationToken>())
            .Throws(new RequestFailedException(401, "Unauthorized access to Azure Key Vault"));

        var validator = CreateValidator();

        var ex = await Assert.ThrowsAsync<KeyVaultStartupException>(() => validator.ValidateAndHydrateSecretsAsync());
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task ValidateAsync_KeyVaultThrowsRequestFailedException404_ShouldThrowException()
    {
        _secretProviderMock.GetSecretAsync("jwt-secret-key", Arg.Any<CancellationToken>())
            .Throws(new RequestFailedException(404, "Secret jwt-secret-key not found in vault"));

        var validator = CreateValidator();

        var ex = await Assert.ThrowsAsync<KeyVaultStartupException>(() => validator.ValidateAndHydrateSecretsAsync());
        Assert.Contains("404", ex.Message);
    }

    [Fact]
    public async Task ValidateAsync_KeyVaultTimesOut_ShouldThrowException()
    {
        _secretProviderMock.GetSecretAsync("jwt-secret-key", Arg.Any<CancellationToken>())
            .Throws(new TimeoutException("Connection to Azure Key Vault timed out"));

        var validator = CreateValidator();

        var ex = await Assert.ThrowsAsync<KeyVaultStartupException>(() => validator.ValidateAndHydrateSecretsAsync());
        Assert.Contains("Timeout", ex.Message);
    }

    [Fact]
    public async Task ValidateAsync_DevelopmentEnvironmentWithoutVaultUri_ShouldSkipValidation()
    {
        _kvOptions.VaultUri = string.Empty;
        Environment.SetEnvironmentVariable("AZURE_KEYVAULT_URL", string.Empty);
        _environmentMock.EnvironmentName.Returns("Development");

        var validator = CreateValidator();

        await validator.ValidateAndHydrateSecretsAsync();

        await _secretProviderMock.DidNotReceive().GetSecretAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
