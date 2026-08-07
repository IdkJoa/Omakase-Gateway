using System;
using System.Threading.Tasks;
using Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace OG.IntegrationTests.Fixtures;

/// <summary>
/// Fábrica de aplicación Web para pruebas de integración extremo a extremo (HU-039 / T-082).
/// Utiliza Testcontainers para instanciar PostgreSQL 16 y Redis 7 efímeros en Docker.
/// </summary>
public class CustomWebApplicationFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>, IAsyncLifetime
    where TEntryPoint : class
{
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("omakasedb_test")
        .WithUsername("omakase_test")
        .WithPassword("TestPassword123!")
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public string PostgresConnectionString => _postgresContainer.GetConnectionString();
    public string RedisConnectionString => _redisContainer.GetConnectionString();

    public async Task InitializeAsync()
    {
        // Iniciar los contenedores Docker efímeros en paralelo
        await Task.WhenAll(
            _postgresContainer.StartAsync(),
            _redisContainer.StartAsync()
        );

        // Asegurar que la BD PostgreSQL tenga el esquema creado y listo
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OmakaseDbContext>();
        await db.Database.EnsureCreatedAsync();

        await SeedInitialDataAsync(db);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(
            _postgresContainer.DisposeAsync().AsTask(),
            _redisContainer.DisposeAsync().AsTask()
        );
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("DISABLE_KEYVAULT_EXIT_FOR_TESTS", "true");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
        builder.UseSetting("ConnectionStrings:Omakase", _postgresContainer.GetConnectionString());
        builder.UseSetting("ConnectionStrings:redis", _redisContainer.GetConnectionString());
    }

    private static async Task SeedInitialDataAsync(OmakaseDbContext db)
    {
        // Sembrar datos iniciales requeridos para las pruebas de integración si la BD está vacía
        if (!await db.ProtectedServices.AnyAsync())
        {
            db.ProtectedServices.Add(new Domain.Entities.ProtectedService
            {
                Id = Domain.ValueObjects.ProtectedServiceId.From(Guid.NewGuid()),
                Name = "testservice",
                UpstreamUrl = "https://httpbin.org",
                IsActive = true,
                RequiresAuth = false,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync();
        }
    }
}
