using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Application.Common.RiskEngine;
using Application.Common.RiskEngine.AnomalyDetection;
using Application.Common.Security;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure;
using Infrastructure.AnomalyDetection;
using Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using OG.Application.UnitTests.Security;
using Xunit;

namespace OG.Application.UnitTests.Persistence;

/// <summary>
/// Pruebas del material de demostración (HU-036 / T-077).
/// <para>
/// Lo que importa aquí no es que siembre filas, sino tres garantías: que no se active fuera
/// de desarrollo, que no duplique nada al volver a arrancar, y que los datos sintéticos sean
/// internamente coherentes. Esto último es lo que más pesa de cara a la defensa: si el
/// veredicto de una fila no se corresponde con su propio desglose de puntajes, cualquiera que
/// cruce las columnas en el Dashboard encuentra una contradicción.
/// </para>
/// </summary>
public class DemoDataSeederTests : IDisposable
{
    private readonly TestDbContext _context;

    public DemoDataSeederTests()
    {
        var options = new DbContextOptionsBuilder<OmakaseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new TestDbContext(options);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private static IConfiguration Config(bool habilitado) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Demo:SeedExtendedData:Enabled"] = habilitado ? "true" : "false",
                ["Demo:SeedExtendedData:ClientPassword"] = "Demo.Omakase-2026!",
            })
            .Build();

    private DemoDataSeeder Sut(bool habilitado)
    {
        var anomalyOptions = new AnomalyDetectionOptions();
        return new DemoDataSeeder(
            _context,
            Config(habilitado),
            new BehaviorBaselineBootstrapper(new FeatureExtractor(anomalyOptions), anomalyOptions),
            Substitute.For<IRedisService>(),
            anomalyOptions);
    }

    /// <summary>Deja el mínimo del que depende el material de demostración.</summary>
    private async Task<UserId> SembrarBaseAsync()
    {
        var admin = new User
        {
            Id       = UserId.New(),
            Username = "admin",
            Type     = UserType.SecurityOfficer,
            IsActive = true,
        };
        _context.Users.Add(admin);
        _context.RiskScoreConfigs.Add(new RiskScoreConfig
        {
            Id                 = RiskScoreConfigId.New(),
            PolicyWeight       = 0.5m,
            AnomalyWeight      = 0.5m,
            ColdStartPenalty   = 30m,
            ColdStartN         = 10,
            ChallengeThreshold = 33m,
            BlockThreshold     = 70m,
        });
        await _context.SaveChangesAsync();
        return admin.Id;
    }

    [Fact]
    public async Task NoSiembraNadaCuandoEstaDeshabilitado()
    {
        await SembrarBaseAsync();

        await Sut(habilitado: false).SeedAsync();

        Assert.Empty(await _context.AccessPolicies.ToListAsync());
        Assert.Empty(await _context.AuditLogs.ToListAsync());
        Assert.Empty(await _context.ProtectedServices.ToListAsync());
    }

    [Fact]
    public async Task NoSiembraNadaSinAdministrador()
    {
        // access_policies.created_by es obligatorio: sin el sembrador base no hay a quién
        // atribuir las políticas, y forzarlo dejaría una clave foránea colgando.
        await Sut(habilitado: true).SeedAsync();

        Assert.Empty(await _context.AccessPolicies.ToListAsync());
    }

    [Fact]
    public async Task SiembraElMaterialQuePideElBacklog()
    {
        await SembrarBaseAsync();

        await Sut(habilitado: true).SeedAsync();

        Assert.Equal(4, await _context.AccessPolicies.CountAsync());
        Assert.Equal(2, await _context.ProtectedServices.CountAsync());
        Assert.Equal(4, await _context.Users.CountAsync(u => u.Type == UserType.Client));
        Assert.Equal(4, await _context.UserBehaviorProfiles.CountAsync());
        Assert.True(await _context.AuditLogs.CountAsync() >= 50);
    }

    [Fact]
    public async Task AsociaConjuntosDeReglasDistintosACadaServicio()
    {
        await SembrarBaseAsync();

        await Sut(habilitado: true).SeedAsync();

        var porServicio = await _context.ServicePolicies
            .Include(sp => sp.ProtectedService)
            .GroupBy(sp => sp.ProtectedService!.Name)
            .Select(g => new { Servicio = g.Key, Politicas = g.Count() })
            .ToListAsync();

        // Es el caso de uso que justifica la tabla de unión: la nómina exige las cuatro
        // comprobaciones y el servicio de reportes solo dos.
        Assert.Equal(4, porServicio.Single(x => x.Servicio == "nomina").Politicas);
        Assert.Equal(2, porServicio.Single(x => x.Servicio == "reportes").Politicas);
    }

    [Fact]
    public async Task LaCuentaDeServicioQuedaMarcadaComoNoInteractiva()
    {
        await SembrarBaseAsync();

        await Sut(habilitado: true).SeedAsync();

        var servicio = await _context.Users.SingleAsync(u => u.Username == "svc.integracion");
        Assert.False(servicio.IsInteractive);

        // Las cuentas humanas sí pueden completar un segundo factor.
        Assert.All(
            await _context.Users.Where(u => u.Username.StartsWith("cliente.")).ToListAsync(),
            u => Assert.True(u.IsInteractive));
    }

    [Fact]
    public async Task ElHistorialDeAuditoriaEsInternamenteCoherente()
    {
        await SembrarBaseAsync();
        var config = await _context.RiskScoreConfigs.SingleAsync();

        await Sut(habilitado: true).SeedAsync();

        foreach (var fila in await _context.AuditLogs.ToListAsync())
        {
            var esperado = Math.Round(Math.Clamp(
                config.PolicyWeight * fila.PolicyScore + config.AnomalyWeight * fila.AnomalyScore,
                0m, 100m), 2);

            Assert.Equal(esperado, fila.RiskScore);

            var veredictoEsperado =
                fila.RiskScore <= config.ChallengeThreshold ? Verdict.Allow :
                fila.RiskScore <= config.BlockThreshold ? Verdict.Challenge : Verdict.Block;

            Assert.Equal(veredictoEsperado, fila.Verdict);
        }
    }

    [Fact]
    public async Task VolverAArrancarNoDuplicaNada()
    {
        await SembrarBaseAsync();
        var sut = Sut(habilitado: true);

        await sut.SeedAsync();
        var politicas = await _context.AccessPolicies.CountAsync();
        var servicios = await _context.ProtectedServices.CountAsync();
        var usuarios = await _context.Users.CountAsync();
        var perfiles = await _context.UserBehaviorProfiles.CountAsync();
        var auditoria = await _context.AuditLogs.CountAsync();

        await sut.SeedAsync();

        Assert.Equal(politicas, await _context.AccessPolicies.CountAsync());
        Assert.Equal(servicios, await _context.ProtectedServices.CountAsync());
        Assert.Equal(usuarios, await _context.Users.CountAsync());
        Assert.Equal(perfiles, await _context.UserBehaviorProfiles.CountAsync());
        Assert.Equal(auditoria, await _context.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task LosIdentificadoresDeEvaluacionSonUnicos()
    {
        await SembrarBaseAsync();

        await Sut(habilitado: true).SeedAsync();

        var ids = await _context.AuditLogs.Select(a => a.EvaluationId).ToListAsync();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
