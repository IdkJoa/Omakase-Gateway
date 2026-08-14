using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Application.Common.Options;
using Application.Features.Auth;
using Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Infrastructure.Security;

/// <summary>
/// Genera tokens JWT HS256 firmados con la clave secreta obtenida de Azure Key Vault
/// (inyectada vía <see cref="JwtOptions"/> desde <see cref="IConfiguration"/>).
/// HU-019 / T-038 / T-039.
/// </summary>
public sealed class GatewayTokenService : IGatewayTokenService
{
    private readonly JwtOptions _options;

    public GatewayTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public (string accessToken, Guid jti) GenerateAccessToken(User user)
    {
        var jti = Guid.NewGuid();
        var keyBytes = Encoding.UTF8.GetBytes(_options.SecretKey);
        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes),
            SecurityAlgorithms.HmacSha256);

        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenTtlMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.Value.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, jti.ToString()),
            new Claim("username", user.Username),
            new Claim("user_type", user.Type.ToString()),
            new Claim(JwtRegisteredClaimNames.Iat,
                now.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = signingCredentials
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(tokenDescriptor);
        return (handler.WriteToken(token), jti);
    }

    /// <inheritdoc/>
    public string GenerateRefreshTokenRaw()
    {
        // UUID v4 opaco: valor plano solo va en la cookie HttpOnly; en PostgreSQL se persiste su hash SHA-256.
        return Guid.NewGuid().ToString("N");
    }

    /// <inheritdoc/>
    public string HashRefreshToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
