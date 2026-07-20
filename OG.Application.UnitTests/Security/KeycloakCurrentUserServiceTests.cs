using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Application.Common.Security;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure;
using Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace OG.Application.UnitTests.Security;

/// <summary>
/// Tests de HU-023 T-047 — puente de identidad Keycloak → tabla <c>users</c>
/// (<see cref="KeycloakCurrentUserService"/>): resolución por <c>keycloak_sub</c>,
/// adopción de la fila sembrada sin vincular y aprovisionamiento just-in-time.
/// </summary>
public class KeycloakCurrentUserServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OmakaseDbContext _db;
    private readonly KeycloakCurrentUserService _sut;

    public KeycloakCurrentUserServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<OmakaseDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TestDbContext(options);
        _db.Database.EnsureCreated();

        _sut = new KeycloakCurrentUserService(
            _db, new LogSanitizer(), NullLogger<KeycloakCurrentUserService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ClaimsPrincipal Principal(string? sub, string? preferredUsername = null)
    {
        var identity = new ClaimsIdentity("TestAuth");
        if (sub is not null)
            identity.AddClaim(new Claim("sub", sub));
        if (preferredUsername is not null)
            identity.AddClaim(new Claim("preferred_username", preferredUsername));
        return new ClaimsPrincipal(identity);
    }

    private User AddUser(string username, string? keycloakSub, UserType type, bool isActive = true)
    {
        var user = new User
        {
            Id           = UserId.New(),
            Username     = username,
            Type         = type,
            PasswordHash = null!,
            KeycloakSub  = keycloakSub!,
            IsActive     = isActive,
        };
        _db.Users.Add(user);
        _db.SaveChanges();
        return user;
    }

    // ── Casos ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SinClaimSub_DevuelveNull()
    {
        var result = await _sut.GetOrProvisionAsync(Principal(sub: null));

        Assert.Null(result);
        Assert.Equal(0, await _db.Users.CountAsync());
    }

    [Fact]
    public async Task FilaYaVinculada_SeResuelvePorKeycloakSub()
    {
        var sub = Guid.NewGuid().ToString();
        var existing = AddUser("admin.omakase", sub, UserType.SecurityOfficer);

        var result = await _sut.GetOrProvisionAsync(Principal(sub, "otro-nombre"));

        Assert.NotNull(result);
        Assert.Equal(existing.Id, result.Id);
        Assert.Equal(1, await _db.Users.CountAsync());
    }

    [Fact]
    public async Task FilaSembradaSinVincular_SeAdoptaYVincula()
    {
        // El seeder crea 'admin' (SecurityOfficer) con keycloak_sub = null.
        var seeded = AddUser("admin", keycloakSub: null, UserType.SecurityOfficer);
        var sub = Guid.NewGuid().ToString();

        var result = await _sut.GetOrProvisionAsync(Principal(sub, "admin"));

        Assert.NotNull(result);
        Assert.Equal(seeded.Id, result.Id);
        Assert.Equal(sub, result.KeycloakSub);
        Assert.Equal(1, await _db.Users.CountAsync());
    }

    [Fact]
    public async Task AdminNuevo_SeAprovisionaJustInTime_YEsIdempotente()
    {
        var sub = Guid.NewGuid().ToString();

        var first = await _sut.GetOrProvisionAsync(Principal(sub, "joel.guerra"));
        var second = await _sut.GetOrProvisionAsync(Principal(sub, "joel.guerra"));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal("joel.guerra", first.Username);
        Assert.Equal(UserType.SecurityOfficer, first.Type);
        Assert.Equal(sub, first.KeycloakSub);
        Assert.Equal(1, await _db.Users.CountAsync());
    }

    [Fact]
    public async Task ClientUserHomonimo_NoSeAdopta_SeCreaFilaAparte()
    {
        // Un client user con el mismo username NUNCA se vincula al sub del admin
        // (escalada de privilegios silenciosa). Se crea fila nueva con el sub como username.
        var client = AddUser("admin", keycloakSub: null, UserType.Client);
        var sub = Guid.NewGuid().ToString();

        var result = await _sut.GetOrProvisionAsync(Principal(sub, "admin"));

        Assert.NotNull(result);
        Assert.NotEqual(client.Id, result.Id);
        Assert.Equal(UserType.SecurityOfficer, result.Type);
        Assert.Equal(sub, result.Username);
        Assert.Equal(2, await _db.Users.CountAsync());

        var untouched = await _db.Users.SingleAsync(u => u.Id == client.Id);
        Assert.Null(untouched.KeycloakSub);
        Assert.Equal(UserType.Client, untouched.Type);
    }

    [Fact]
    public async Task CuentaDesactivada_DevuelveNull_FailClosed()
    {
        var sub = Guid.NewGuid().ToString();
        AddUser("admin.baja", sub, UserType.SecurityOfficer, isActive: false);

        var result = await _sut.GetOrProvisionAsync(Principal(sub, "admin.baja"));

        Assert.Null(result);
    }
}
