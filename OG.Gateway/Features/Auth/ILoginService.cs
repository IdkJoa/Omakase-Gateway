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
}
