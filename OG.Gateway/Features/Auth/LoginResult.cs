using Domain.Entities;

namespace Application.Features.Auth;

/// <summary>
/// Resultado interno del proceso de login, usado para comunicar el estado
/// entre la capa de aplicación y el endpoint de Minimal API.
/// </summary>
public sealed record LoginResult
{
    // ── Éxito ────────────────────────────────────────────────────────────────
    public string? AccessToken { get; init; }
    /// <summary>Valor plano del refresh token (solo en el momento de creación; no se persiste).</summary>
    public string? RefreshTokenRaw { get; init; }

    // ── Errores ───────────────────────────────────────────────────────────────
    public bool IsUnauthorized { get; init; }
    public bool IsLocked { get; init; }
    /// <summary>Segundos restantes de bloqueo cuando <see cref="IsLocked"/> es <c>true</c>.</summary>
    public int LockedSecondsRemaining { get; init; }

    // ── Fábricas ──────────────────────────────────────────────────────────────
    public static LoginResult Success(string accessToken, string refreshToken) =>
        new() { AccessToken = accessToken, RefreshTokenRaw = refreshToken };

    public static LoginResult Unauthorized() =>
        new() { IsUnauthorized = true };

    public static LoginResult Locked(DateTimeOffset lockedUntil) =>
        new()
        {
            IsLocked = true,
            LockedSecondsRemaining = (int)Math.Ceiling(
                (lockedUntil - DateTimeOffset.UtcNow).TotalSeconds)
        };
}
