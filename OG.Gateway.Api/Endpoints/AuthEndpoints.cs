using Application.Common.Security;
using Application.Features.Auth;
using Application.Features.Auth.DTOs;

namespace OmakaseGateway.Api.Endpoints;

/// <summary>
/// Grupo de endpoints de autenticación para Client Users (JWT propio del Gateway).
/// HU-019 / T-038 / T-039.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>Nombre de la cookie del refresh token.</summary>
    private const string RefreshTokenCookieName = "refresh_token";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        // POST /auth/login
        group.MapPost("/login", async (
            LoginRequest request,
            ILoginService loginService,
            ILogSanitizer sanitizer,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            // T-039: capturar User-Agent para persistirlo como device_info.
            // FIX HU-046: sanitizado (ILogSanitizer, SRS §6.3.2) y acotado a 255
            // (longitud de refresh_tokens.device_info) antes de persistir/auditar.
            var deviceInfo = ctx.Request.Headers.UserAgent.ToString();
            deviceInfo = string.IsNullOrWhiteSpace(deviceInfo)
                ? null
                : sanitizer.Sanitize(deviceInfo, maxLength: 255);

            // T-040: IP real del cliente (ya resuelta por UseForwardedHeaders middleware)
            var sourceIp = ctx.Connection.RemoteIpAddress?.ToString();

            var result = await loginService.LoginAsync(request, deviceInfo, sourceIp, ct);

            if (result.IsLocked)
            {
                return Results.Json(
                    new AccountLockedResponse(
                        "Cuenta bloqueada temporalmente por intentos fallidos excesivos.",
                        result.LockedSecondsRemaining),
                    statusCode: StatusCodes.Status423Locked);
            }

            if (result.IsUnauthorized)
                return Results.Unauthorized();

            // T-039: Set-Cookie: refresh_token=<UUID v4>; HttpOnly; Secure; SameSite=Strict; Path=/auth/refresh
            ctx.Response.Cookies.Append(RefreshTokenCookieName, result.RefreshTokenRaw!, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/auth/refresh",
                Expires = DateTimeOffset.UtcNow.AddDays(7)
            });

            return Results.Ok(new LoginResponse(result.AccessToken!));
        })
        .WithName("Login")
        .WithSummary("Autenticación de Client Users")
        .WithDescription(
            "Valida credenciales, gestiona bloqueo de cuenta y emite un JWT HS256 (TTL 15 min) " +
            "más un refresh token UUID v4 en cookie HttpOnly (TTL 7 días). T-038 / T-039.")
        .Produces<LoginResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces<AccountLockedResponse>(StatusCodes.Status423Locked)
        .AllowAnonymous();

        // POST /auth/refresh
        group.MapPost("/refresh", async (
            ILoginService loginService,
            ILogSanitizer sanitizer,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            if (!ctx.Request.Cookies.TryGetValue(RefreshTokenCookieName, out var rawRefreshToken))
                return Results.Unauthorized();

            var deviceInfo = ctx.Request.Headers.UserAgent.ToString();
            deviceInfo = string.IsNullOrWhiteSpace(deviceInfo)
                ? null
                : sanitizer.Sanitize(deviceInfo, maxLength: 255);
            var sourceIp = ctx.Connection.RemoteIpAddress?.ToString();

            var result = await loginService.RefreshSessionAsync(rawRefreshToken, deviceInfo, sourceIp, ct);

            if (result.IsUnauthorized)
                return Results.Unauthorized();

            // Set new cookie
            ctx.Response.Cookies.Append(RefreshTokenCookieName, result.RefreshTokenRaw!, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/auth/refresh",
                Expires = DateTimeOffset.UtcNow.AddDays(7)
            });

            return Results.Ok(new LoginResponse(result.AccessToken!));
        })
        .WithName("Refresh")
        .WithSummary("Renueva la sesión")
        .WithDescription("Renueva el JWT usando un refresh token válido (T-041).")
        .Produces<LoginResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .AllowAnonymous(); // The refresh token is in the cookie, no JWT needed here.

        // POST /auth/logout
        group.MapPost("/logout", async (
            ILoginService loginService,
            ILogSanitizer sanitizer,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            // FIX HU-020: la sesión se identifica por los claims del access token, NO por la cookie.
            // La cookie del refresh token vive en Path=/auth/refresh (SRS §3.6) y nunca se envía a
            // /auth/logout: condicionar el logout a su presencia hacía que no revocara nada.
            var userId = ctx.User.FindFirst("sub")?.Value
                      ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var jti = ctx.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
            var expClaim = ctx.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Exp)?.Value;

            TimeSpan remainingLifetime = TimeSpan.Zero;
            if (long.TryParse(expClaim, out var expUnix))
            {
                var expDateTime = DateTimeOffset.FromUnixTimeSeconds(expUnix);
                remainingLifetime = expDateTime - DateTimeOffset.UtcNow;
            }

            var deviceInfo = ctx.Request.Headers.UserAgent.ToString();
            deviceInfo = string.IsNullOrWhiteSpace(deviceInfo)
                ? null
                : sanitizer.Sanitize(deviceInfo, maxLength: 255);
            var sourceIp = ctx.Connection.RemoteIpAddress?.ToString();

            await loginService.LogoutAsync(
                userId ?? string.Empty,
                jti ?? string.Empty,
                remainingLifetime,
                deviceInfo,
                sourceIp,
                ct);

            ctx.Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions
            {
                Path = "/auth/refresh"
            });

            return Results.NoContent();
        })
        .WithName("Logout")
        .WithSummary("Cierra sesión")
        .WithDescription("Revoca el refresh token y añade el access token a la blacklist (T-042).")
        .Produces(StatusCodes.Status204NoContent)
        .RequireAuthorization(); // Requires valid JWT

        return app;
    }
}

