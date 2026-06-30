using Infrastructure;
using Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// ── Aspire ServiceDefaults ────────────────────────────────────────────────────
// Registra OpenTelemetry (traces + metrics + logs), health checks y
// service discovery automático entre recursos del AppHost.
builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<OmakaseDbContext>("Omakase");
builder.AddRedisClient("redis");
builder.Services.AddOpenApi();

builder.Services.AddInfrastructure();

// Configurar ForwardedHeaders (HU-010 / T-019)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    
    // Por defecto en desarrollo confiamos en cualquier proxy de Docker/Aspire.
    // En producción se restringe mediante KnownProxies en appsettings.json.
    var configSection = builder.Configuration.GetSection("ForwardedHeaders");
    var trustAll = configSection.GetValue<bool>("TrustAll", true);
    
    if (trustAll)
    {
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    }
    else
    {
        var knownProxies = configSection.GetSection("KnownProxies").Get<string[]>();
        if (knownProxies != null)
        {
            foreach (var proxy in knownProxies)
            {
                if (System.Net.IPAddress.TryParse(proxy, out var ip))
                {
                    options.KnownProxies.Add(ip);
                }
            }
        }
    }
});

var app = builder.Build();

// Habilitar el procesamiento de cabeceras reenviadas antes de cualquier middleware (T-019)
app.UseForwardedHeaders();

// Habilitar el control de tasa de peticiones (Rate Limiting) por IP (T-020)
app.UseMiddleware<Application.Middlewares.RateLimitMiddleware>();

// Migraciones automáticas al arranque 
// Aplica las migraciones pendientes antes de aceptar tráfico.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OmakaseDbContext>();
    await db.Database.MigrateAsync();

    var seeder = scope.ServiceProvider.GetRequiredService<IDbSeeder>();
    await seeder.SeedAsync();
}

// Endpoints de diagnostico Aspire (/health y /alive) 
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();