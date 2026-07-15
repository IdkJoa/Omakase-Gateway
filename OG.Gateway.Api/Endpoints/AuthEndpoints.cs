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
            HttpContext ctx,
            CancellationToken ct) =>
        {
            // T-039: capturar User-Agent para persistirlo como device_info
            var deviceInfo = ctx.Request.Headers.UserAgent.ToString();
            if (string.IsNullOrWhiteSpace(deviceInfo))
                deviceInfo = null;

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

        return app;
    }
}

