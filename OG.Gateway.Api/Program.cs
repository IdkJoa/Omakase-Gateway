using Infrastructure;
using Microsoft.EntityFrameworkCore;
using ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// ── Aspire ServiceDefaults ────────────────────────────────────────────────────
// Registra OpenTelemetry (traces + metrics + logs), health checks y
// service discovery automático entre recursos del AppHost.
builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<OmakaseDbContext>("Omakase");
builder.AddRedisClient("redis");
builder.Services.AddOpenApi();

// ── Infrastructure (repositorios, etc.) — se completa en HU-003/004 ──────────
// builder.Services.AddInfrastructure();

var app = builder.Build();

// Migraciones automáticas al arranque 
// Aplica las migraciones pendientes antes de aceptar tráfico.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OmakaseDbContext>();
    await db.Database.MigrateAsync();
}

// Endpoints de diagnostico Aspire (/health y /alive) 
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();