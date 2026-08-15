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

// Caso de uso de login para Client Users. Bloquea la cuenta 30 min tras 5 intentos fallidos.
public sealed class LoginService : ILoginService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(30);

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
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username, ct);

        if (user is null)
        {
            _logger.LogWarning("Login fallido: usuario '{Username}' no encontrado.", request.Username);
            EmitAudit(userId: null, verdict: Verdict.Block, rules: RuleLoginFailed,
                sourceIp: sourceIp, userAgent: deviceInfo);
            return LoginResult.Unauthorized();
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("Login fallido: usuario '{Username}' inactivo.", request.Username);
            EmitAudit(user.Id, Verdict.Block, RuleLoginFailed, sourceIp: sourceIp, userAgent: deviceInfo);
            return LoginResult.Unauthorized();
        }

        // FIX HU-046: los SECURITY_OFFICER (Keycloak) tienen password_hash NULL;
        // sin este guard, BCrypt.Verify(null) lanza excepción → HTTP 500.
        if (user.Type != UserType.Client || string.IsNullOrEmpty(user.PasswordHash))
        {
            _logger.LogWarning(
                "Login fallido: usuario '{Username}' no es Client User con credencial local.",
                user.Username);
            EmitAudit(user.Id, Verdict.Block, RuleLoginFailed, sourceIp: sourceIp, userAgent: deviceInfo);
            return LoginResult.Unauthorized();
        }

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTimeOffset.UtcNow)
        {
            _logger.LogWarning(
                "Login bloqueado: usuario '{Username}', locked_until={LockedUntil}.",
                user.Username, user.LockedUntil.Value);
            EmitAudit(user.Id, Verdict.Block, RuleLoginBlocked, sourceIp: sourceIp, userAgent: deviceInfo);
            return LoginResult.Locked(user.LockedUntil.Value);
        }

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

                EmitAudit(user.Id, Verdict.Block, RuleLoginBlocked, sourceIp: sourceIp, userAgent: deviceInfo);
                return LoginResult.Locked(user.LockedUntil.Value);
            }

            _logger.LogWarning(
                "Login fallido: contraseña incorrecta para '{Username}'. Intento {Attempt}/{Max}.",
                user.Username, user.FailedAttempts, MaxFailedAttempts);
            await _db.SaveChangesAsync(ct);

            EmitAudit(user.Id, Verdict.Block, RuleLoginFailed, sourceIp: sourceIp, userAgent: deviceInfo);
            return LoginResult.Unauthorized();
        }

        user.FailedAttempts = 0;
        user.LockedUntil = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        var (accessToken, _) = _tokenService.GenerateAccessToken(user);
        var refreshTokenRaw = _tokenService.GenerateRefreshTokenRaw();
        var refreshTokenHash = _tokenService.HashRefreshToken(refreshTokenRaw);

        // Solo el hash SHA-256 del UUID v4 se guarda en la BD; el valor plano
        // se envía únicamente en la cookie HttpOnly y nunca se re-almacena.
        var refreshToken = new RefreshToken
        {
            Id = RefreshTokenId.New(),
            UserId = user.Id,
            TokenHash = refreshTokenHash,
            DeviceInfo = deviceInfo,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            IsRevoked = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.RefreshTokens.Add(refreshToken);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Login exitoso: usuario '{Username}' (Id={UserId}).",
            user.Username, user.Id);

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

        // RefreshToken.User se declara User? pero la relación es REQUERIDA: la FK (RefreshToken.UserId)
        // es un struct no anulable, así que EF la trata como obligatoria y el Include de arriba genera
        // un INNER JOIN. Si storedToken no es null, User tampoco puede serlo — verificado insertando
        // una fila huérfana con la FK desactivada: la consulta sin Include la encuentra y la consulta
        // con Include la descarta. El ! documenta esa invariante en lugar de añadir una rama muerta.
        var user = storedToken.User!;

        if (storedToken.ExpiresAt <= DateTimeOffset.UtcNow || !user.IsActive)
        {
            _logger.LogWarning("Intento de refresh con token expirado o usuario inactivo.");
            EmitAudit(user.Id, Verdict.Block, JsonDocument.Parse("[\"AUTH_REFRESH_FAILED\"]"), sourceIp, deviceInfo);
            return LoginResult.Unauthorized();
        }

        // Si el token está revocado, es señal de posible robo/reuso: revocar TODOS los tokens del usuario.
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

        storedToken.IsRevoked = true;

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

    /// <inheritdoc/>
    /// <remarks>
    /// FIX HU-020: la cookie del refresh token se emite con <c>Path=/auth/refresh</c> (SRS §3.6) y
    /// nunca llega a <c>/auth/logout</c>, así que la sesión se identifica por el claim <c>sub</c> del
    /// access token. Al no poder distinguir qué refresh token es el del dispositivo actual, se
    /// revocan todos los del usuario (fail-closed, Zero Trust SRS §9.4: ninguna sesión sobrevive).
    /// </remarks>
    public async Task LogoutAsync(
        string userId,
        string accessTokenJti,
        TimeSpan accessTokenRemainingLifetime,
        string? deviceInfo = null,
        string? sourceIp = null,
        CancellationToken ct = default)
    {
        // Revocar el access token en Redis es lo prioritario: es la credencial en uso.
        if (!string.IsNullOrEmpty(accessTokenJti) && accessTokenRemainingLifetime > TimeSpan.Zero)
        {
            await _redisService.AddToBlacklistAsync(accessTokenJti, accessTokenRemainingLifetime);
        }

        if (!Guid.TryParse(userId, out var userGuid))
        {
            _logger.LogWarning("Logout con claim sub inválido; no se revocaron refresh tokens.");
            return;
        }

        var ownerId = UserId.From(userGuid);

        var activeTokens = await _db.RefreshTokens
            .Where(rt => rt.UserId == ownerId && !rt.IsRevoked)
            .ToListAsync(ct);

        foreach (var token in activeTokens)
            token.IsRevoked = true;

        if (activeTokens.Count > 0)
            await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Logout: usuario {UserId}, {Count} refresh token(s) revocado(s), access token en lista negra.",
            ownerId, activeTokens.Count);

        EmitAudit(ownerId, Verdict.Allow, JsonDocument.Parse("[\"AUTH_LOGOUT\"]"), sourceIp, deviceInfo);
    }

    // Encola el evento en el canal de auditoría sin bloquear el flujo de login;
    // AuditPersistenceWorker lo persiste asíncronamente (fire-and-forget).
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

