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

// Tests de HU-019 (autenticación JWT propia del Gateway): T-038 login, T-039 refresh, T-040 bloqueo + auditoría.
public class LoginServiceTests : IDisposable
{
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

    // Escenario 1: login exitoso (HU-019 criterio 1)

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
        Assert.NotEqual(result.RefreshTokenRaw, stored!.TokenHash);
        Assert.Equal(64, stored.TokenHash.Length);
        Assert.Matches("^[0-9a-f]+$", stored.TokenHash);
        Assert.Equal("TestAgent/1.0", stored.DeviceInfo);
        Assert.True(stored.ExpiresAt > DateTimeOffset.UtcNow.AddDays(6));
        Assert.False(stored.IsRevoked);
    }

    [Fact]
    public async Task Login_ConCredencialesValidas_ResetearFailedAttemptsYLockedUntil()
    {
        var user = SeedUser(failedAttempts: 3,
            lockedUntil: DateTimeOffset.UtcNow.AddMinutes(-5)); // bloqueo ya expirado

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

    // Escenario 2: credenciales inválidas y bloqueo progresivo (HU-019 criterio 2)

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
        // Sin usuario creado: la respuesta debe ser idéntica a la de contraseña incorrecta (no enumeración de usuarios)
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
        SeedUser(failedAttempts: 4); // este 5to intento dispara el bloqueo
        var antes = DateTimeOffset.UtcNow;

        var result = await _sut.LoginAsync(new LoginRequest("johndoe", "WrongPassword!"));

        Assert.True(result.IsLocked);
        Assert.False(result.IsUnauthorized);
        Assert.Null(result.AccessToken);
        Assert.True(result.LockedSecondsRemaining > 0);

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
        SeedUser(failedAttempts: 3); // 4to intento: todavía por debajo del umbral de bloqueo

        await _sut.LoginAsync(new LoginRequest("johndoe", "WrongPassword!"));

        _db.ChangeTracker.Clear();
        var updated = await _db.Users.FirstOrDefaultAsync(u => u.Username == "johndoe");
        Assert.Equal(4, updated!.FailedAttempts);
        Assert.Null(updated.LockedUntil);
    }

    // Escenario 3: cuenta bloqueada (HU-019 criterio 3)

    [Fact]
    public async Task Login_ConCuentaBloqueada_Retorna423_AunConContrasenaCorrecta()
    {
        // Credenciales correctas pero locked_until en el futuro: la cuenta sigue bloqueada
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
        Assert.True(result.LockedSecondsRemaining <= 1800);
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
        // locked_until en el pasado: el bloqueo ya expiró
        SeedUser(lockedUntil: DateTimeOffset.UtcNow.AddMinutes(-1));

        var result = await _sut.LoginAsync(new LoginRequest("johndoe", RawPassword));

        Assert.False(result.IsLocked);
        Assert.False(result.IsUnauthorized);
        Assert.NotNull(result.AccessToken);
    }

    // GatewayTokenService: T-038 / T-039

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

        Assert.Equal(100, tokens.Count);
    }

    // Escenario 4: refresh session (HU-020 / T-041)

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
        SeedRefreshToken(user.Id, "active-1");
        SeedRefreshToken(user.Id, "active-2");
        SeedRefreshToken(user.Id, "active-3");

        var revokedRaw = "revoked-token";
        SeedRefreshToken(user.Id, revokedRaw, isRevoked: true);

        var result = await _sut.RefreshSessionAsync(revokedRaw);

        Assert.True(result.IsUnauthorized);
        
        var allTokens = await _db.RefreshTokens.Where(t => t.UserId == user.Id).ToListAsync();
        Assert.All(allTokens, t => Assert.True(t.IsRevoked));
        
        _auditChannel.Received(1).TryWrite(Arg.Is<AuditEvent>(e => e.Verdict == Verdict.Block && e.TriggeredRules!.RootElement.ToString().Contains("AUTH_TOKEN_COMPROMISED")));
    }

    // Escenario 5: logout (HU-020 / T-042)
    // El logout identifica la sesión por el claim `sub` del access token, no por la cookie de refresh
    // (Path=/auth/refresh, SRS §3.6, nunca llega a /auth/logout); condicionarlo a esa cookie dejaba
    // vivos ambos tokens pese a responder 204 (fix HU-020).

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
        // Sin cookie no se puede singularizar el dispositivo actual, así que revoca todas las sesiones (fail-closed, SRS §9.4)
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
        // Regresión: el refresh token seguía activo tras logout, permitiendo renovar con la cookie robada
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

    private static int GuidVersion(Guid guid)
    {
        // En little-endian de .NET, el byte 7 del array tiene el nibble de versión en bits [4..7]
        var bytes = guid.ToByteArray();
        return (bytes[7] >> 4) & 0xF;
    }
}
