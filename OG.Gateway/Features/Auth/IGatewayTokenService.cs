using Domain.Entities;

namespace Application.Features.Auth;

/// <summary>
/// Servicio de generación de tokens JWT propios del Gateway (HS256).
/// La implementación concreta (<c>GatewayTokenService</c>) reside en Infrastructure.
/// </summary>
public interface IGatewayTokenService
{
    /// <summary>
    /// Genera un access token HS256 firmado con la clave de Azure Key Vault.
    /// </summary>
    /// <param name="user">Entidad de dominio del usuario autenticado.</param>
    /// <returns>
    /// Tupla con:
    /// <list type="bullet">
    ///   <item><c>accessToken</c> — JWT firmado listo para enviar al cliente.</item>
    ///   <item><c>jti</c> — UUID del claim <c>jti</c> para registro y revocación en Redis.</item>
    /// </list>
    /// </returns>
    (string accessToken, Guid jti) GenerateAccessToken(User user);

    /// <summary>
    /// Genera un refresh token opaco criptográficamente seguro (32 bytes, Base64Url).
    /// El valor plano se devuelve para ser enviado en la cookie; el hash SHA-256
    /// debe persistirse en PostgreSQL.
    /// </summary>
    /// <returns>El valor plano del refresh token.</returns>
    string GenerateRefreshTokenRaw();

    /// <summary>
    /// Computa el hash SHA-256 del valor plano del refresh token para persistencia segura.
    /// </summary>
    string HashRefreshToken(string rawToken);
}
