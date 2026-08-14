using Domain.Entities;

namespace Application.Features.Auth;

public interface IGatewayTokenService
{
    /// <summary>Devuelve también el <c>jti</c> del token para registro y revocación en Redis.</summary>
    (string accessToken, Guid jti) GenerateAccessToken(User user);

    /// <summary>Genera un refresh token opaco (32 bytes, Base64Url); el valor plano no se persiste, solo su hash SHA-256.</summary>
    string GenerateRefreshTokenRaw();

    string HashRefreshToken(string rawToken);
}
