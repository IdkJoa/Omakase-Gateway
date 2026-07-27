using Application;
using Application.Middlewares;
using Infrastructure;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using OG.Gateway.Api.Endpoints;
using OG.Gateway.Api.Extensions;
using OmakaseGateway.Api.Endpoints;
using ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// ── Aspire ServiceDefaults & Data Resources ──────────────────────────────────
builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<OmakaseDbContext>("Omakase");
builder.AddRedisClient("redis");

// ── 1. Capa de Aplicación (Mediador, Comandos, Opciones & Reglas de Negocio) ─
builder.Services.AddGatewayApplication(builder.Configuration);

// ── 2. Capa de Infraestructura (EF Core, Redis, ML.NET, Seeder, GeoLocation) ─
builder.Services.AddInfrastructure();

// ── 3. Capa de API / Presentación (YARP, Forwarded Headers, Auth & OpenAPI) ────
builder.Services.AddGatewayApiConfiguration(builder.Configuration);

var app = builder.Build();

// ── Middleware Pipeline ────────────────────────────────────────────────────────
app.UseForwardedHeaders();
app.UseMiddleware<RateLimitMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<RiskEvaluationMiddleware>();

// ── Migraciones & Seed Data al arranque en Desarrollo ────────────────────────
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OmakaseDbContext>();
    await db.Database.MigrateAsync();

    var seeder = scope.ServiceProvider.GetRequiredService<IDbSeeder>();
    await seeder.SeedAsync();
}

// ── Endpoints & YARP ──────────────────────────────────────────────────────────
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapAuthEndpoints();
app.MapMfaEndpoints();
app.MapReverseProxy();

await app.RunAsync();