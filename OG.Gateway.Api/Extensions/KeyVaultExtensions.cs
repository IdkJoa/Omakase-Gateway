using Infrastructure.KeyVault;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace OG.Gateway.Api.Extensions;

public static class KeyVaultExtensions
{
    /// <summary>Fail-closed (T-068/HU-031): timeout, 401, 403, 404 o error de red al leer Key Vault termina el proceso.</summary>
    public static async Task ValidateKeyVaultOnStartupAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<KeyVaultStartupValidator>();
        await validator.ValidateAndHydrateSecretsAsync();
    }
}
