using System.Text.Json;
using Application.Common.Audit;
using Application.Features.Auth;
using Application.Features.Auth.DTOs;
using BC = BCrypt.Net.BCrypt;
using Domain.Entities;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Security;

/// <summary>
/// Implementación del caso de uso de login para Client Users.
/// HU-019 / T-038 / T-039 / T-040.
/// </summary>
/// <remarks>
/// Flujo:
/// 1. Buscar usuario por username.
/// 2. Verificar is_active.
/// 3. Verificar locked_until → HTTP 423 si está bloqueado.
/// 4. Verificar contraseña con BCrypt (factor ≥12).
///    - Fallo: incrementar failed_attempts; bloquear si llega a 5 → HTTP 423.
///    - Éxito: reset failed_attempts + locked_until.
/// 5. Emitir access token (JWT HS256, jti UUID) + refresh token (UUID v4 opaco).
/// 6. Persistir refresh token: SHA-256 del valor plano + device_info + expires_at.
/// 7. Registrar evento de auditoría (T-040) vía IAuditChannel (fire-and-forget).
/// </remarks>
public sealed class LoginService : ILoginService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(30);

    // Reglas de autenticación para TriggeredRules en audit_logs
    private static readonly JsonDocument RuleLoginSuccess =
        JsonDocument.Parse("[\"AUTH_LOGIN_SUCCESS\"]");
    private static readonly JsonDocument RuleLoginFailed =
        JsonDocument.Parse("[\"AUTH_LOGIN_FAILED\"]");
    private static readonly JsonDocument RuleLoginBlocked =
        JsonDocument.Parse("[\"AUTH_ACCOUNT_LOCKED\"]");

    private readonly OmakaseDbContext _db;
    private readonly IGatewayTokenService _tokenService;
    private readonly IAuditChannel _auditChannel;
    private readonly ILogger<LoginService> _logger;
    private readonly Application.Common.Security.IRedisService _redisService;

    public LoginService(
        OmakaseDbContext db,
        IGatewayTokenService tokenService,
        IAuditChannel auditChannel,
        ILogger<LoginService> logger,
        Application.Common.Security.IRedisService redisService)
    {
        _db = db;
        _tokenService = tokenService;
        _auditChannel = auditChannel;
        _logger = logger;
        _redisService = redisService;
    }

    public async Task<LoginResult> LoginAsync(
        LoginRequest request,
        string? deviceInfo = null,
        string? sourceIp = null,
        CancellationToken ct = default)
    {
        // ── 1. Buscar usuario ─────────────────────────────────────────────────
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username, ct);

        if (user is null)
        {
            _logger.LogWarning("Login fallido: usuario '{Username}' no encontrado.", request.Username);
            EmitAudit(userId: null, verdict: Verdict.Block, rules: RuleLoginFailed,
                sourceIp: sourceIp, userAgent: deviceInfo);
            return LoginResult.Unauthorized();
        }

        // ── 2. Verificar cuenta activa ────────────────────────────────────────
        if (!user.IsActive)
        {
            _logger.LogWarning("Login fallido: usuario '{Username}' inactivo.", request.Username);
            EmitAudit(user.Id, Verdict.Block, RuleLoginFailed, sourceIp: sourceIp, userAgent: deviceInfo);
            return LoginResult.Unauthorized();
        }

        // ── 3. Verificar bloqueo ──────────────────────────────────────────────
        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTimeOffset.UtcNow)
        {
            _logger.LogWarning(
                "Login bloqueado: usuario '{Username}', locked_until={LockedUntil}.",
                user.Username, user.LockedUntil.Value);
            EmitAudit(user.Id, Verdict.Block, RuleLoginBlocked, sourceIp: sourceIp, userAgent: deviceInfo);
            return LoginResult.Locked(user.LockedUntil.Value);
        }

        // ── 4. Verificar contraseña ───────────────────────────────────────────
        var passwordValid = BC.Verify(request.Password, user.PasswordHash);

        if (!passwordValid)
        {
            user.FailedAttempts++;
            user.UpdatedAt = DateTimeOffset.UtcNow;

            if (user.FailedAttempts >= MaxFailedAttempts)
            {
                user.LockedUntil = DateTimeOffset.UtcNow.Add(LockDuration);
                _logger.LogWarning(
                    "Cuenta bloqueada: usuario '{Username}' alcanzó {MaxAttempts} intentos fallidos.",
                    user.Username, MaxFailedAttempts);
                await _db.SaveChangesAsync(ct);

                // T-040: evento de bloqueo (cuenta recién bloqueada en este intento)
                EmitAudit(user.Id, Verdict.Block, RuleLoginBlocked, sourceIp: sourceIp, userAgent: deviceInfo);
                return LoginResult.Locked(user.LockedUntil.Value);
            }

            _logger.LogWarning(
                "Login fallido: contraseña incorrecta para '{Username}'. Intento {Attempt}/{Max}.",
                user.Username, user.FailedAttempts, MaxFailedAttempts);
            await _db.SaveChangesAsync(ct);

            // T-040: evento de credenciales inválidas
            EmitAudit(user.Id, Verdict.Block, RuleLoginFailed, sourceIp: sourceIp, userAgent: deviceInfo);
            return LoginResult.Unauthorized();
        }

        // ── 5. Login exitoso — reset de contadores ────────────────────────────
        user.FailedAttempts = 0;
        user.LockedUntil = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        // ── 6. Emitir tokens ──────────────────────────────────────────────────
        var (accessToken, _) = _tokenService.GenerateAccessToken(user);
        var refreshTokenRaw = _tokenService.GenerateRefreshTokenRaw();
        var refreshTokenHash = _tokenService.HashRefreshToken(refreshTokenRaw);

        // ── 7. Persistir refresh token (T-039) ────────────────────────────────
        // Solo el hash SHA-256 del UUID v4 se guarda en la BD; el valor plano
        // se envía únicamente en la cookie HttpOnly y nunca se re-almacena.
        var refreshToken = new RefreshToken
        {
            Id = RefreshTokenId.New(),
            UserId = user.Id,
            TokenHash = refreshTokenHash,
            DeviceInfo = deviceInfo,          // User-Agent capturado en el endpoint
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            IsRevoked = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.RefreshTokens.Add(refreshToken);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Login exitoso: usuario '{Username}' (Id={UserId}).",
            user.Username, user.Id);

        // T-040: evento de éxito (Allow)
        EmitAudit(user.Id, Verdict.Allow, RuleLoginSuccess, sourceIp: sourceIp, userAgent: deviceInfo);

        return LoginResult.Success(accessToken, refreshTokenRaw);
    }

    public async Task<LoginResult> RefreshSessionAsync(
        string rawRefreshToken,
        string? deviceInfo = null,
        string? sourceIp = null,
        CancellationToken ct = default)
    {
        var tokenHash = _tokenService.HashRefreshToken(rawRefreshToken);
        var storedToken = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, ct);

        if (storedToken is null)
        {
            _logger.LogWarning("Intento de refresh con token no encontrado.");
            EmitAudit(null, Verdict.Block, JsonDocument.Parse("[\"AUTH_REFRESH_FAILED\"]"), sourceIp, deviceInfo);
            return LoginResult.Unauthorized();
        }

        var user = storedToken.User;

        // Verificar si el token ya expiró o el usuario fue desactivado
        if (storedToken.ExpiresAt <= DateTimeOffset.UtcNow || !user.IsActive)
        {
            _logger.LogWarning("Intento de refresh con token expirado o usuario inactivo.");
            EmitAudit(user.Id, Verdict.Block, JsonDocument.Parse("[\"AUTH_REFRESH_FAILED\"]"), sourceIp, deviceInfo);
            return LoginResult.Unauthorized();
        }

        // T-041: Si el token está revocado, revocar TODOS los tokens del usuario (alerta de compromiso)
        if (storedToken.IsRevoked)
        {
            _logger.LogWarning("ALERTA DE SEGURIDAD: Intento de uso de refresh token revocado. Revocando todos los tokens del usuario {UserId}.", user.Id);
            
            var allActiveTokens = await _db.RefreshTokens
                .Where(rt => rt.UserId == user.Id && !rt.IsRevoked)
                .ToListAsync(ct);
            
            foreach (var token in allActiveTokens)
            {
                token.IsRevoked = true;
            }
            
            await _db.SaveChangesAsync(ct);
            EmitAudit(user.Id, Verdict.Block, JsonDocument.Parse("[\"AUTH_TOKEN_COMPROMISED\"]"), sourceIp, deviceInfo);
            
            return LoginResult.Unauthorized();
        }

        // Rotar: Marcar el actual como revocado
        storedToken.IsRevoked = true;

        // Generar nuevo par de tokens
        var (accessToken, _) = _tokenService.GenerateAccessToken(user);
        var refreshTokenRaw = _tokenService.GenerateRefreshTokenRaw();
        var newRefreshTokenHash = _tokenService.HashRefreshToken(refreshTokenRaw);

        var newRefreshToken = new RefreshToken
        {
            Id = RefreshTokenId.New(),
            UserId = user.Id,
            TokenHash = newRefreshTokenHash,
            DeviceInfo = deviceInfo,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            IsRevoked = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.RefreshTokens.Add(newRefreshToken);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Refresh de sesión exitoso: usuario '{Username}'.", user.Username);
        EmitAudit(user.Id, Verdict.Allow, JsonDocument.Parse("[\"AUTH_REFRESH_SUCCESS\"]"), sourceIp, deviceInfo);

        return LoginResult.Success(accessToken, refreshTokenRaw);
    }

    public async Task LogoutAsync(
        string rawRefreshToken,
        string accessTokenJti,
        TimeSpan accessTokenRemainingLifetime,
        string? deviceInfo = null,
        string? sourceIp = null,
        CancellationToken ct = default)
    {
        var tokenHash = _tokenService.HashRefreshToken(rawRefreshToken);
        var storedToken = await _db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, ct);

        if (storedToken is not null)
        {
            storedToken.IsRevoked = true;
            await _db.SaveChangesAsync(ct);
            
            EmitAudit(storedToken.UserId, Verdict.Allow, JsonDocument.Parse("[\"AUTH_LOGOUT\"]"), sourceIp, deviceInfo);
        }

        if (!string.IsNullOrEmpty(accessTokenJti) && accessTokenRemainingLifetime > TimeSpan.Zero)
        {
            // T-042: Añadir a la blacklist en Redis
            await _redisService.AddToBlacklistAsync(accessTokenJti, accessTokenRemainingLifetime);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Encola un evento de autenticación en el canal de auditoría (fire-and-forget).
    /// No bloquea el flujo de login; el <see cref="AuditPersistenceWorker"/> lo persiste asíncronamente.
    /// T-040.
    /// </summary>
    private void EmitAudit(
        UserId? userId,
        Verdict verdict,
        JsonDocument rules,
        string? sourceIp,
        string? userAgent)
    {
        var auditEvent = new AuditEvent(
            EvaluationId: Guid.NewGuid(),
            SourceIp: sourceIp ?? "unknown",
            UserAgent: userAgent,
            UserId: userId?.Value.ToString(),
            Verdict: verdict,
            RiskScore: verdict == Verdict.Allow ? 0m : 100m,
            PolicyScore: verdict == Verdict.Allow ? 0m : 100m,
            AnomalyScore: 0m,
            TraceId: System.Diagnostics.Activity.Current?.TraceId.ToString() ?? string.Empty,
            EvaluatedAt: DateTimeOffset.UtcNow,
            TriggeredRules: rules);

        if (!_auditChannel.TryWrite(auditEvent))
        {
            _logger.LogWarning(
                "[T-040] Canal de auditoría lleno — evento de login descartado. Verdict={Verdict}",
                verdict);
        }
    }
}

