using Infrastructure.KeyVault;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace OG.Gateway.Api.Extensions;

/// <summary>
/// Métodos de extensión para ejecutar la validación de Azure Key Vault en el arranque (T-068 / HU-031).
/// </summary>
public static class KeyVaultExtensions
{
    /// <summary>
    /// Intenta consultar los secretos obligatorios desde Azure Key Vault durante el arranque.
    /// Si falla por timeout, 401, 403, 404 o error de red, TERMINA el proceso inmediatamente (Fail-Closed).
    /// </summary>
    public static async Task ValidateKeyVaultOnStartupAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<KeyVaultStartupValidator>();
        await validator.ValidateAndHydrateSecretsAsync();
    }
}
