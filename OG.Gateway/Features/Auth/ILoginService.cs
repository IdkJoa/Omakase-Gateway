using Application.Features.Auth.DTOs;

namespace Application.Features.Auth;

public interface ILoginService
{
    Task<LoginResult> LoginAsync(
        LoginRequest request,
        string? deviceInfo = null,
        string? sourceIp = null,
        CancellationToken ct = default);

    Task<LoginResult> RefreshSessionAsync(
        string rawRefreshToken,
        string? deviceInfo = null,
        string? sourceIp = null,
        CancellationToken ct = default);

    /// <summary>
    /// La sesión se identifica por <paramref name="userId"/> y no por la cookie: el refresh token
    /// se emite con <c>Path=/auth/refresh</c> (SRS §3.6), por lo que nunca acompaña a /auth/logout.
    /// </summary>
    Task LogoutAsync(
        string userId,
        string accessTokenJti,
        TimeSpan accessTokenRemainingLifetime,
        string? deviceInfo = null,
        string? sourceIp = null,
        CancellationToken ct = default);
}
