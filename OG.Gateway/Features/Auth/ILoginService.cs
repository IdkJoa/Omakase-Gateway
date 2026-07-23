using Application.Features.Auth.DTOs;

namespace Application.Features.Auth;

/// <summary>
/// Puerto de aplicación para el caso de uso de autenticación de Client Users.
/// HU-019 / T-038 / T-039 / T-040.
/// </summary>
public interface ILoginService
{
    /// <summary>
    /// Valida las credenciales del usuario, gestiona el bloqueo de cuenta y,
    /// si el login es exitoso, emite un access token y un refresh token.
    /// </summary>
    /// <param name="request">Credenciales del usuario.</param>
    /// <param name="deviceInfo">
    /// Información del dispositivo/cliente (User-Agent). Se persiste en
    /// <c>refresh_tokens.device_info</c> y en <c>audit_logs.user_agent</c>.
    /// </param>
    /// <param name="sourceIp">
    /// IP de origen del cliente (ya resuelta por <c>UseForwardedHeaders</c>).
    /// Se persiste en <c>audit_logs.source_ip</c> (T-040).
    /// </param>
    /// <param name="ct">Token de cancelación.</param>
    Task<LoginResult> LoginAsync(
        LoginRequest request,
        string? deviceInfo = null,
        string? sourceIp = null,
        CancellationToken ct = default);

    /// <summary>
    /// Renueva la sesión utilizando un refresh token, invalidando el anterior.
    /// T-041.
    /// </summary>
    Task<LoginResult> RefreshSessionAsync(
        string rawRefreshToken,
        string? deviceInfo = null,
        string? sourceIp = null,
        CancellationToken ct = default);

    /// <summary>
    /// Cierra la sesión: revoca los refresh tokens activos del usuario y añade el access token
    /// a la lista negra de Redis. T-042.
    /// </summary>
    /// <param name="userId">
    /// Identificador del usuario, tomado del claim <c>sub</c> del access token.
    /// La sesión se identifica por el usuario y NO por la cookie: la cookie del refresh token
    /// se emite con <c>Path=/auth/refresh</c> (exigido por el SRS §3.6), por lo que nunca
    /// acompaña a una petición a <c>/auth/logout</c>.
    /// </param>
    /// <param name="accessTokenJti">Claim <c>jti</c> del access token a revocar.</param>
    /// <param name="accessTokenRemainingLifetime">TTL de la entrada en la lista negra (exp − now).</param>
    Task LogoutAsync(
        string userId,
        string accessTokenJti,
        TimeSpan accessTokenRemainingLifetime,
        string? deviceInfo = null,
        string? sourceIp = null,
        CancellationToken ct = default);
}
