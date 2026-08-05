using System;
using System.Text.Json;
using System.Threading.Tasks;
using Application.Common.Security;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OG.Application.UnitTests.Security;
using Xunit;

namespace OG.Application.UnitTests.Persistence;

/// <summary>
/// Tests de <see cref="LastAccessService"/> (HU-014 / T-027): el ancla contra la que se mide
/// el Viaje Imposible. Solo los accesos CONCEDIDOS y geolocalizados sirven de referencia.
/// </summary>
public class LastAccessServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OmakaseDbContext _db;
    private readonly LastAccessService _sut;
    private readonly User _user;

    // Coordenadas reales usadas por los escenarios de HU-034.
    private const double SantoDomingoLat = 18.4861, SantoDomingoLon = -69.9312;
    private const double TokioLat = 35.6762, TokioLon = 139.6503;

    public LastAccessServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<OmakaseDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new TestDbContext(options);
        _db.Database.EnsureCreated();

        _user = new User
        {
            Id = UserId.New(),
            Username = "victima",
            Type = UserType.Client,
            PasswordHash = "hash",
            KeycloakSub = null!,
            IsActive = true,
        };
        _db.Users.Add(_user);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        _sut = new LastAccessService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private void SeedAccess(Verdict verdict, DateTimeOffset at, double? lat = null, double? lon = null)
    {
        JsonDocument? geo = null;
        if (lat.HasValue && lon.HasValue)
        {
            // Mismo formato camelCase que escribe EvaluateRiskHandler.BuildGeoAsync.
            geo = JsonSerializer.SerializeToDocument(new
            {
                country = "XX",
                city = "ciudad",
                latitude = lat.Value,
                longitude = lon.Value,
            });
        }

        _db.AuditLogs.Add(new AuditLog
        {
            Id = AuditLogId.New(),
            EvaluationId = Guid.NewGuid(),
            UserId = _user.Id,
            SourceIp = "203.0.113.7",
            Geo = geo,
            Verdict = verdict,
            EvaluatedAt = at,
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private Task<LastAccessResult?> Act() => _sut.GetLastAccessAsync(_user.Id.Value.ToString());

    [Fact]
    public async Task SinHistorial_DevuelveNull()
    {
        Assert.Null(await Act());
    }

    [Fact]
    public async Task ConAccesoConcedido_DevuelveSusCoordenadas()
    {
        var momento = DateTimeOffset.UtcNow.AddHours(-1);
        SeedAccess(Verdict.Allow, momento, SantoDomingoLat, SantoDomingoLon);

        var result = await Act();

        Assert.NotNull(result);
        Assert.Equal(SantoDomingoLat, result!.Latitude, precision: 4);
        Assert.Equal(SantoDomingoLon, result.Longitude, precision: 4);
    }

    /// <summary>
    /// Regresión del bypass: antes se tomaba como ancla el último log con geo SIN mirar el veredicto.
    /// Eso permitía a un atacante desactivar la regla con una petición desechable — la primera desde
    /// su ubicación se bloqueaba pero movía el ancla, y la siguiente ya parecía plausible.
    /// </summary>
    [Fact]
    public async Task AccesoBloqueadoPosterior_NoDesplazaElAncla()
    {
        SeedAccess(Verdict.Allow, DateTimeOffset.UtcNow.AddHours(-1), SantoDomingoLat, SantoDomingoLon);
        SeedAccess(Verdict.Block, DateTimeOffset.UtcNow.AddMinutes(-1), TokioLat, TokioLon);

        var result = await Act();

        Assert.NotNull(result);
        Assert.Equal(SantoDomingoLat, result!.Latitude, precision: 4);
        Assert.Equal(SantoDomingoLon, result.Longitude, precision: 4);
    }

    [Fact]
    public async Task AccesoDesafiadoPosterior_NoDesplazaElAncla()
    {
        SeedAccess(Verdict.Allow, DateTimeOffset.UtcNow.AddHours(-1), SantoDomingoLat, SantoDomingoLon);
        SeedAccess(Verdict.Challenge, DateTimeOffset.UtcNow.AddMinutes(-1), TokioLat, TokioLon);

        var result = await Act();

        Assert.NotNull(result);
        Assert.Equal(SantoDomingoLat, result!.Latitude, precision: 4);
    }

    [Fact]
    public async Task SoloAccesosDenegados_DevuelveNull()
    {
        SeedAccess(Verdict.Block, DateTimeOffset.UtcNow.AddMinutes(-5), TokioLat, TokioLon);
        SeedAccess(Verdict.Challenge, DateTimeOffset.UtcNow.AddMinutes(-1), TokioLat, TokioLon);

        Assert.Null(await Act());
    }

    [Fact]
    public async Task AccesoConcedidoSinGeo_SeIgnora()
    {
        SeedAccess(Verdict.Allow, DateTimeOffset.UtcNow.AddHours(-1), SantoDomingoLat, SantoDomingoLon);
        SeedAccess(Verdict.Allow, DateTimeOffset.UtcNow.AddMinutes(-1)); // sin geo

        var result = await Act();

        Assert.NotNull(result);
        Assert.Equal(SantoDomingoLat, result!.Latitude, precision: 4);
    }

    [Fact]
    public async Task EntreVariosConcedidos_DevuelveElMasReciente()
    {
        SeedAccess(Verdict.Allow, DateTimeOffset.UtcNow.AddHours(-2), TokioLat, TokioLon);
        SeedAccess(Verdict.Allow, DateTimeOffset.UtcNow.AddMinutes(-10), SantoDomingoLat, SantoDomingoLon);

        var result = await Act();

        Assert.NotNull(result);
        Assert.Equal(SantoDomingoLat, result!.Latitude, precision: 4);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-es-un-guid")]
    public async Task UserIdInvalido_DevuelveNull(string userId)
    {
        Assert.Null(await _sut.GetLastAccessAsync(userId));
    }
}
