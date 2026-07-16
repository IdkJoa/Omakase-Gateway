using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Application.Common.Audit;
using Application.Common.Options;
using Application.Features.Auth;
using Application.Features.Auth.DTOs;
using BC = BCrypt.Net.BCrypt;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure;
using Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace OG.Application.UnitTests.Security;

/// <summary>
/// Tests de HU-019 — Autenticación de Client Users con JWT propio del Gateway.
/// Cubre T-038 (login / access token), T-039 (refresh token / cookie) y T-040 (bloqueo + auditoría).
///
/// Estrategia de persistencia:
///   SqliteTestFixture mantiene una conexión SQLite en memoria compartida.
///   TestDbContext añade ValueConverters de JsonDocument para compatibilidad con SQLite.
/// </summary>
public class LoginServiceTests : IDisposable
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private readonly SqliteConnection _connection;
    private readonly OmakaseDbContext _db;
    private readonly IGatewayTokenService _tokenService;
    private readonly IAuditChannel _auditChannel;
    private readonly global::Application.Common.Security.IRedisService _redisService;
    private readonly LoginService _sut;

    /// <summary>BCrypt factor 4 — rápido en tests; producción usa ≥12.</summary>
    private const string RawPassword = "Test@1234";
    private static readonly string HashedPassword = BC.HashPassword(RawPassword, workFactor: 4);

    private static readonly JwtOptions TestJwtOptions = new()
    {
        SecretKey = "supersecret-test-key-that-is-long-enough-32chars!!",
        Issuer    = "test-issuer",
        Audience  = "test-audience",
        AccessTokenTtlMinutes = 15,
        RefreshTokenTtlDays   = 7
    };

    public LoginServiceTests()
    {
        // Conexión nueva per test para asegurar aislamiento completo
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<OmakaseDbContext>()
            .UseSqlite(_connection)
            .Options;
        
        _db = new TestDbContext(options);
        _db.Database.EnsureCreated();

        _tokenService = new GatewayTokenService(Options.Create(TestJwtOptions));

        _auditChannel = Substitute.For<IAuditChannel>();
        _auditChannel.TryWrite(Arg.Any<AuditEvent>()).Returns(true);

        _redisService = Substitute.For<global::Application.Common.Security.IRedisService>();

        _sut = new LoginService(_db, _tokenService, _auditChannel, NullLogger<LoginService>.Instance, _redisService);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private User SeedUser(
        string username = "johndoe",
        int failedAttempts = 0,
        DateTimeOffset? lockedUntil = null,
        bool isActive = true)
    {
        var user = new User
        {
            Id = UserId.New(),
            Username = username,
            PasswordHash = HashedPassword,
            Type = UserType.Client,
            IsActive = isActive,
            FailedAttempts = failedAttempts,
            LockedUntil = lockedUntil
        };
        _db.Users.Add(user);
        _db.SaveChanges();
        _db.ChangeTracker.Clear(); // evitar conflictos de tracking entre operaciones
        return user;
    }

    private RefreshToken SeedRefreshToken(UserId userId, string rawToken, bool isRevoked = false, DateTimeOffset? expiresAt = null)
    {
        var tokenHash = _tokenService.HashRefreshToken(rawToken);
        var token = new RefreshToken
        {
            Id = RefreshTokenId.New(),
            UserId = userId,
            TokenHash = tokenHash,
            DeviceInfo = "TestDevice",
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(7),
            IsRevoked = isRevoked,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.RefreshTokens.Add(token);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return token;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ESCENARIO 1 — Login exitoso (HU-019 Criterio 1)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Login_ConCredencialesValidas_RetornaAccessToken()
    {
        SeedUser();

        var result = await _sut.LoginAsync(new LoginRequest("johndoe", RawPassword));

        Assert.False(result.IsUnauthorized);
        Assert.False(result.IsLocked);
        Assert.NotNull(result.AccessToken);
        Assert.NotEmpty(result.AccessToken!);
    }

    [Fact]
    public async Task Login_ConCredencialesValidas_AccessTokenEsJwtTresParts()
    {
        SeedUser();

        var result = await _sut.LoginAsync(new LoginRequest("johndoe", RawPassword));

        // JWT = header.payload.signature
        Assert.Equal(3, result.AccessToken!.Split('.').Length);
    }

    [Fact]
    public async Task Login_ConCredencialesValidas_AccessTokenContieneClaimsRequeridos()
    {
        var user = SeedUser();

        var result = await _sut.LoginAsync(new LoginRequest("johndoe", RawPassword));

        var payload = DecodeJwtPayload(result.AccessToken!);
        Assert.Equal(user.Id.Value.ToString(), payload.GetProperty("sub").GetString());
        Assert.Equal("johndoe", payload.GetProperty("username").GetString());
        Assert.Equal(nameof(UserType.Client), payload.GetProperty("user_type").GetString());
        Assert.True(payload.TryGetProperty("jti", out var jti));
        Assert.True(Guid.TryParse(jti.GetString(), out _), "jti debe ser un UUID válido.");
    }

    [Fact]
    public async Task Login_ConCredencialesValidas_RefreshTokenEsUuidV4()
    {
        SeedUser();

        var result = await _sut.LoginAsync(new LoginRequest("johndoe", RawPassword));

        // UUID v4 en formato "N" = 32 hex chars sin guiones
        Assert.NotNull(result.RefreshTokenRaw);
        Assert.Equal(32, result.RefreshTokenRaw!.Length);
        Assert.True(Guid.TryParseExact(result.RefreshTokenRaw, "N", out var parsed));
        Assert.Equal(4, GuidVersion(parsed));
    }

    [Fact]
    public async Task Login_ConCredencialesValidas_PersisteSoloHashDelRefreshToken()
    {
        var user = SeedUser();

        var result = await _sut.LoginAsync(
            new LoginRequest("johndoe", RawPassword),
            deviceInfo: "TestAgent/1.0");

        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(r => r.UserId == user.Id);

        Assert.NotNull(stored);
        // El hash NO debe ser igual al valor plano
        Assert.NotEqual(result.RefreshTokenRaw, stored!.TokenHash);
        // El hash debe ser un SHA-256 (64 hex chars)
        Assert.Equal(64, stored.TokenHash.Length);
        Assert.Matches("^[0-9a-f]+$", stored.TokenHash);
        // device_info persistido correctamente
        Assert.Equal("TestAgent/1.0", stored.DeviceInfo);
        // TTL correcto
        Assert.True(stored.ExpiresAt > DateTimeOffset.UtcNow.AddDays(6));
        Assert.False(stored.IsRevoked);
    }

    [Fact]
    public async Task Login_ConCredencialesValidas_ResetearFailedAttemptsYLockedUntil()
    {
        var user = SeedUser(failedAttempts: 3,
            lockedUntil: DateTimeOffset.UtcNow.AddMinutes(-5)); // bloqueo expirado

        await _sut.LoginAsync(new LoginRequest("johndoe", RawPassword));

        var updated = await _db.Users.FindAsync(user.Id);
        Assert.Equal(0, updated!.FailedAttempts);
        Assert.Null(updated.LockedUntil);
    }

    [Fact]
    public async Task Login_ConCredencialesValidas_EmiteAuditEventAllow_T040()
    {
        SeedUser();

        await _sut.LoginAsync(
            new LoginRequest("johndoe", RawPassword),
            sourceIp: "192.168.1.100");

        _auditChannel.Received(1).TryWrite(
            Arg.Is<AuditEvent>(e =>
                e.Verdict == Verdict.Allow &&
                e.SourceIp == "192.168.1.100" &&
                e.TriggeredRules != null &&
                e.TriggeredRules.RootElement.ToString().Contains("AUTH_LOGIN_SUCCESS")));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ESCENARIO 2 — Credenciales inválidas y bloqueo progresivo (HU-019 Criterio 2)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Login_ConContrasenaIncorrecta_Retorna401()
    {
        SeedUser();

        var result = await _sut.LoginAsync(new LoginRequest("johndoe", "WrongPassword!"));

        Assert.True(result.IsUnauthorized);
        Assert.False(result.IsLocked);
        Assert.Null(result.AccessToken);
    }

    [Fact]
    public async Task Login_ConContrasenaIncorrecta_IncrementaFailedAttempts()
    {
        var user = SeedUser(failedAttempts: 0);

        await _sut.LoginAsync(new LoginRequest("johndoe", "WrongPassword!"));

        _db.ChangeTracker.Clear();
        var updated = await _db.Users.FindAsync(user.Id);
        Assert.Equal(1, updated!.FailedAttempts);
    }

    [Fact]
    public async Task Login_ConUsuarioInexistente_Retorna401_SinFiltrarInfo()
    {
        // No se crea ningún usuario — la respuesta debe ser idéntica a contraseña incorrecta
        var result = await _sut.LoginAsync(new LoginRequest("ghost", "anypassword"));

        Assert.True(result.IsUnauthorized);
        Assert.False(result.IsLocked);
    }

    [Fact]
    public async Task Login_ConCuentaInactiva_Retorna401()
    {
        SeedUser(isActive: false);

        var result = await _sut.LoginAsync(new LoginRequest("johndoe", RawPassword));

        Assert.True(result.IsUnauthorized);
        Assert.False(result.IsLocked);
    }

    [Fact]
    public async Task Login_Al5toIntentoFallido_BloqueaCuenta30Minutos_T040()
    {
        SeedUser(failedAttempts: 4); // el 5to intento lo dispara este test
        var antes = DateTimeOffset.UtcNow;

        var result = await _sut.LoginAsync(new LoginRequest("johndoe", "WrongPassword!"));

        // Resultado HTTP 423
        Assert.True(result.IsLocked);
        Assert.False(result.IsUnauthorized);
        Assert.Null(result.AccessToken);
        Assert.True(result.LockedSecondsRemaining > 0);

        // locked_until persistido correctamente (~30 min)
        _db.ChangeTracker.Clear();
        var updated = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == "johndoe");
        Assert.NotNull(updated!.LockedUntil);
        Assert.InRange(updated.LockedUntil!.Value,
            antes.AddMinutes(29),
            antes.AddMinutes(31));
    }

    [Fact]
    public async Task Login_Al5toIntentoFallido_EmiteAuditEventAccountLocked_T040()
    {
        SeedUser(failedAttempts: 4);

        await _sut.LoginAsync(new LoginRequest("johndoe", "WrongPassword!"));

        _auditChannel.Received(1).TryWrite(
            Arg.Is<AuditEvent>(e =>
                e.Verdict == Verdict.Block &&
                e.TriggeredRules!.RootElement.ToString().Contains("AUTH_ACCOUNT_LOCKED")));
    }

    [Fact]
    public async Task Login_ConContrasenaIncorrecta_EmiteAuditEventFailed_T040()
    {
        SeedUser();

        await _sut.LoginAsync(
            new LoginRequest("johndoe", "WrongPassword!"),
            sourceIp: "10.0.0.99");

        _auditChannel.Received(1).TryWrite(
            Arg.Is<AuditEvent>(e =>
                e.Verdict == Verdict.Block &&
                e.SourceIp == "10.0.0.99" &&
                e.TriggeredRules!.RootElement.ToString().Contains("AUTH_LOGIN_FAILED")));
    }

    [Fact]
    public async Task Login_Intento1A4_NoBloquea_SiSigueDebajo5()
    {
        SeedUser(failedAttempts: 3); // 4to intento (sigue sin bloqueo)

        await _sut.LoginAsync(new LoginRequest("johndoe", "WrongPassword!"));

        _db.ChangeTracker.Clear();
        var updated = await _db.Users.FirstOrDefaultAsync(u => u.Username == "johndoe");
        Assert.Equal(4, updated!.FailedAttempts);
        Assert.Null(updated.LockedUntil); // todavía sin bloqueo
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ESCENARIO 3 — Cuenta bloqueada (HU-019 Criterio 3)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Login_ConCuentaBloqueada_Retorna423_AunConContrasenaCorrecta()
    {
        // locked_until en el futuro — las credenciales son correctas pero la cuenta está bloqueada
        SeedUser(lockedUntil: DateTimeOffset.UtcNow.AddMinutes(15));

        var result = await _sut.LoginAsync(new LoginRequest("johndoe", RawPassword));

        Assert.True(result.IsLocked);
        Assert.False(result.IsUnauthorized);
        Assert.Null(result.AccessToken);
    }

    [Fact]
    public async Task Login_ConCuentaBloqueada_RetornaTiempoRestanteEnSegundos()
    {
        SeedUser(lockedUntil: DateTimeOffset.UtcNow.AddMinutes(30));

        var result = await _sut.LoginAsync(new LoginRequest("johndoe", "WrongPassword!"));

        Assert.True(result.LockedSecondsRemaining > 0);
        Assert.True(result.LockedSecondsRemaining <= 1800); // ≤30 min en segundos
    }

    [Fact]
    public async Task Login_ConCuentaBloqueada_EmiteAuditEventAccountLocked_T040()
    {
        SeedUser(lockedUntil: DateTimeOffset.UtcNow.AddMinutes(20));

        await _sut.LoginAsync(new LoginRequest("johndoe", RawPassword));

        _auditChannel.Received(1).TryWrite(
            Arg.Is<AuditEvent>(e =>
                e.Verdict == Verdict.Block &&
                e.TriggeredRules!.RootElement.ToString().Contains("AUTH_ACCOUNT_LOCKED")));
    }

    [Fact]
    public async Task Login_ConBloqueoExpirado_PermiteLoginExitoso()
    {
        // locked_until en el PASADO → el bloqueo ya expiró, debe permitir el login
        SeedUser(lockedUntil: DateTimeOffset.UtcNow.AddMinutes(-1));

        var result = await _sut.LoginAsync(new LoginRequest("johndoe", RawPassword));

        Assert.False(result.IsLocked);
        Assert.False(result.IsUnauthorized);
        Assert.NotNull(result.AccessToken);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GatewayTokenService — T-038 / T-039
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TokenService_GeneraAccessToken_ConClaimsSubJtiUsernameUserType()
    {
        var user = new User
        {
            Id = UserId.New(),
            Username = "alice",
            PasswordHash = HashedPassword,
            Type = UserType.Client,
            IsActive = true
        };
        var service = new GatewayTokenService(Options.Create(TestJwtOptions));

        var (accessToken, jti) = service.GenerateAccessToken(user);

        Assert.Equal(3, accessToken.Split('.').Length);
        Assert.NotEqual(Guid.Empty, jti);

        var payload = DecodeJwtPayload(accessToken);
        Assert.Equal(user.Id.Value.ToString(), payload.GetProperty("sub").GetString());
        Assert.Equal(jti.ToString(), payload.GetProperty("jti").GetString());
        Assert.Equal("alice", payload.GetProperty("username").GetString());
        Assert.Equal(nameof(UserType.Client), payload.GetProperty("user_type").GetString());
    }

    [Fact]
    public void TokenService_GeneraAccessToken_ConIssuerYAudience()
    {
        var user = new User
        {
            Id = UserId.New(), Username = "bob",
            PasswordHash = HashedPassword, Type = UserType.Client, IsActive = true
        };
        var service = new GatewayTokenService(Options.Create(TestJwtOptions));

        var (accessToken, _) = service.GenerateAccessToken(user);
        var payload = DecodeJwtPayload(accessToken);

        Assert.Equal("test-issuer", payload.GetProperty("iss").GetString());
        Assert.Equal("test-audience", payload.GetProperty("aud").GetString());
    }

    [Fact]
    public void TokenService_GeneraRefreshToken_EsUuidV4Format_N()
    {
        var service = new GatewayTokenService(Options.Create(TestJwtOptions));

        var raw = service.GenerateRefreshTokenRaw();

        Assert.Equal(32, raw.Length);
        Assert.True(Guid.TryParseExact(raw, "N", out var parsed),
            "El refresh token debe ser un UUID v4 en formato 'N'.");
        Assert.Equal(4, GuidVersion(parsed));
    }

    [Fact]
    public void TokenService_HashRefreshToken_ProduceSha256_64HexChars()
    {
        var service = new GatewayTokenService(Options.Create(TestJwtOptions));
        var raw = service.GenerateRefreshTokenRaw();

        var hash = service.HashRefreshToken(raw);

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]+$", hash);
        Assert.NotEqual(raw, hash);
    }

    [Fact]
    public void TokenService_HashRefreshToken_EsDeterministico()
    {
        var service = new GatewayTokenService(Options.Create(TestJwtOptions));
        var raw = service.GenerateRefreshTokenRaw();

        Assert.Equal(service.HashRefreshToken(raw), service.HashRefreshToken(raw));
    }

    [Fact]
    public void TokenService_CadaRefreshToken_EsUnico()
    {
        var service = new GatewayTokenService(Options.Create(TestJwtOptions));

        var tokens = new System.Collections.Generic.HashSet<string>();
        for (var i = 0; i < 100; i++)
            tokens.Add(service.GenerateRefreshTokenRaw());

        Assert.Equal(100, tokens.Count); // sin colisiones en 100 generaciones
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ESCENARIO 4 — Refresh Session (HU-020 / T-041)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Refresh_ConTokenValido_RenuevaTokensYMarcaAnteriorComoRevocado()
    {
        var user = SeedUser();
        var rawToken = "valid-refresh-token-1234567890ab";
        SeedRefreshToken(user.Id, rawToken);

        var result = await _sut.RefreshSessionAsync(rawToken);

        Assert.False(result.IsUnauthorized);
        Assert.NotNull(result.AccessToken);
        Assert.NotNull(result.RefreshTokenRaw);
        
        var oldHash = _tokenService.HashRefreshToken(rawToken);
        var oldStored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == oldHash);
        Assert.True(oldStored!.IsRevoked);
        
        var newHash = _tokenService.HashRefreshToken(result.RefreshTokenRaw!);
        var newStored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == newHash);
        Assert.NotNull(newStored);
        Assert.False(newStored.IsRevoked);
        
        _auditChannel.Received(1).TryWrite(Arg.Is<AuditEvent>(e => e.TriggeredRules!.RootElement.ToString().Contains("AUTH_REFRESH_SUCCESS")));
    }

    [Fact]
    public async Task Refresh_ConTokenInexistente_RetornaUnauthorized()
    {
        var result = await _sut.RefreshSessionAsync("invalid-token");

        Assert.True(result.IsUnauthorized);
        _auditChannel.Received(1).TryWrite(Arg.Is<AuditEvent>(e => e.TriggeredRules!.RootElement.ToString().Contains("AUTH_REFRESH_FAILED")));
    }

    [Fact]
    public async Task Refresh_ConTokenExpirado_RetornaUnauthorized()
    {
        var user = SeedUser();
        var rawToken = "expired-token";
        SeedRefreshToken(user.Id, rawToken, expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        var result = await _sut.RefreshSessionAsync(rawToken);

        Assert.True(result.IsUnauthorized);
        _auditChannel.Received(1).TryWrite(Arg.Is<AuditEvent>(e => e.TriggeredRules!.RootElement.ToString().Contains("AUTH_REFRESH_FAILED")));
    }

    [Fact]
    public async Task Refresh_ConTokenRevocado_RevocaTodosLosTokensYRetornaUnauthorized()
    {
        var user = SeedUser();
        // Tokens activos
        SeedRefreshToken(user.Id, "active-1");
        SeedRefreshToken(user.Id, "active-2");
        SeedRefreshToken(user.Id, "active-3");
        
        // Token revocado
        var revokedRaw = "revoked-token";
        SeedRefreshToken(user.Id, revokedRaw, isRevoked: true);

        var result = await _sut.RefreshSessionAsync(revokedRaw);

        Assert.True(result.IsUnauthorized);
        
        var allTokens = await _db.RefreshTokens.Where(t => t.UserId == user.Id).ToListAsync();
        Assert.All(allTokens, t => Assert.True(t.IsRevoked));
        
        _auditChannel.Received(1).TryWrite(Arg.Is<AuditEvent>(e => e.Verdict == Verdict.Block && e.TriggeredRules!.RootElement.ToString().Contains("AUTH_TOKEN_COMPROMISED")));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ESCENARIO 5 — Logout (HU-020 / T-042)
    // ══════════════════════════════════════════════════════════════════════════

    // El logout identifica la sesión por el claim `sub` del access token, NO por la cookie del
    // refresh token: esa cookie se emite con Path=/auth/refresh (SRS §3.6) y nunca llega a
    // /auth/logout. Condicionar el logout a su presencia dejaba vivos el access token y el
    // refresh token pese a responder 204 (fix HU-020).

    [Fact]
    public async Task Logout_ConTokenValido_RevocaTokenYAgregaAccessABlacklist()
    {
        var user = SeedUser();
        var rawToken = "token-to-logout";
        SeedRefreshToken(user.Id, rawToken);
        var jti = Guid.NewGuid().ToString();
        var lifetime = TimeSpan.FromMinutes(10);

        await _sut.LogoutAsync(user.Id.Value.ToString(), jti, lifetime);

        var hash = _tokenService.HashRefreshToken(rawToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
        Assert.True(stored!.IsRevoked);

        await _redisService.Received(1).AddToBlacklistAsync(jti, lifetime);
        _auditChannel.Received(1).TryWrite(Arg.Is<AuditEvent>(e => e.TriggeredRules!.RootElement.ToString().Contains("AUTH_LOGOUT")));
    }

    [Fact]
    public async Task Logout_RevocaTodasLasSesionesActivasDelUsuario()
    {
        // Sin la cookie no se puede singularizar el token del dispositivo actual, así que el
        // logout revoca todas las sesiones activas (fail-closed, SRS §9.4).
        var user = SeedUser();
        SeedRefreshToken(user.Id, "sesion-1");
        SeedRefreshToken(user.Id, "sesion-2");
        SeedRefreshToken(user.Id, "sesion-3");

        await _sut.LogoutAsync(user.Id.Value.ToString(), Guid.NewGuid().ToString(), TimeSpan.FromMinutes(10));

        var tokens = await _db.RefreshTokens.Where(t => t.UserId == user.Id).ToListAsync();
        Assert.Equal(3, tokens.Count);
        Assert.All(tokens, t => Assert.True(t.IsRevoked));
    }

    [Fact]
    public async Task Logout_NoDejaSesionesQueSobrevivanAlCierre()
    {
        // Regresión del bug: el refresh token seguía activo tras el logout y permitía
        // renovar la sesión con la cookie robada.
        var user = SeedUser();
        var rawToken = "sesion-robada";
        SeedRefreshToken(user.Id, rawToken);

        await _sut.LogoutAsync(user.Id.Value.ToString(), Guid.NewGuid().ToString(), TimeSpan.FromMinutes(10));

        var result = await _sut.RefreshSessionAsync(rawToken);

        Assert.True(result.IsUnauthorized);
    }

    [Fact]
    public async Task Logout_ConUsuarioInexistente_EsIdempotenteYAgregaBlacklist()
    {
        var jti = Guid.NewGuid().ToString();
        var lifetime = TimeSpan.FromMinutes(10);

        await _sut.LogoutAsync(UserId.New().Value.ToString(), jti, lifetime);

        await _redisService.Received(1).AddToBlacklistAsync(jti, lifetime);
    }

    [Fact]
    public async Task Logout_ConSubInvalido_AunAsiRevocaElAccessToken()
    {
        var jti = Guid.NewGuid().ToString();
        var lifetime = TimeSpan.FromMinutes(10);

        await _sut.LogoutAsync("no-es-un-guid", jti, lifetime);

        // La lista negra es la defensa crítica: no depende de poder resolver al usuario.
        await _redisService.Received(1).AddToBlacklistAsync(jti, lifetime);
    }

    // ── Utilidades ────────────────────────────────────────────────────────────

    private static JsonElement DecodeJwtPayload(string jwt)
    {
        var segment = jwt.Split('.')[1];
        var padded = segment.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "=";  break;
        }
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        return JsonDocument.Parse(json).RootElement;
    }

    /// <summary>Extrae el número de versión de UUID del nibble correspondiente.</summary>
    private static int GuidVersion(Guid guid)
    {
        // En little-endian de .NET, el byte 7 del array tiene el nibble de versión en bits [4..7]
        var bytes = guid.ToByteArray();
        return (bytes[7] >> 4) & 0xF;
    }
}
