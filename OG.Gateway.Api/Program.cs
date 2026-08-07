using Application;
using Application.Middlewares;
using Infrastructure;
using Infrastructure.Security;
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

// Fail-closed (SRS §6.3.1 / T-068): en Producción la clave que firma los JWT propios DEBE venir de
// Azure Key Vault — ≥32 bytes y sin el placeholder commiteado. Si no, el proceso NO arranca (nunca
// corre prod con una clave forjable). En Development se permite el marcador reproducible del appsettings.
builder.Services.AddOptions<Application.Common.Options.JwtOptions>()
    .Validate(o => builder.Environment.IsDevelopment()
                   || (!string.IsNullOrWhiteSpace(o.SecretKey)
                       && !o.SecretKey.Contains("CHANGE_ME")
                       && System.Text.Encoding.UTF8.GetByteCount(o.SecretKey) >= 32),
        "Jwt:SecretKey invalida para Produccion: inyecte una clave >=32 bytes desde Azure Key Vault (T-068), no el placeholder.")
    .ValidateOnStart();

// ── 2. Capa de Infraestructura (EF Core, Redis, ML.NET, Seeder, GeoLocation) ─
builder.Services.AddInfrastructure();

// ── 3. Capa de API / Presentación (YARP, Forwarded Headers, Auth & OpenAPI) ────
builder.Services.AddGatewayApiConfiguration(builder.Configuration);

var app = builder.Build();

// ── Middleware Pipeline ────────────────────────────────────────────────────────
app.UseForwardedHeaders();

app.UseCors(ApiExtensions.FrontendCorsPolicy);
app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseMiddleware<RateLimitMiddleware>();

// Cliente de demostración (HU-048), servido solo en desarrollo desde wwwroot/demo.
// Va después del limitador de tasa —no se le exime de esa defensa— y antes de la
// autenticación, porque la página es pública: quien la abre todavía no tiene sesión.
// El motor de riesgo no la evalúa: '/demo' es uno de los nombres reservados del Gateway
// (ProtectedService.ReservedNames), de modo que ni se proxea ni puede registrarse un
// servicio protegido que lo eclipse.
if (app.Environment.IsDevelopment())
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<RiskEvaluationMiddleware>();

// ── Migraciones & Seed Data al arranque en Desarrollo ────────────────────────
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OmakaseDbContext>();
    await db.Database.MigrateAsync();

    // GetServices y no GetRequiredService: hay más de un sembrador registrado y todos deben
    // correr, en el orden en que se registraron (Infrastructure.DependencyInjection).
    foreach (var seeder in scope.ServiceProvider.GetServices<IDbSeeder>())
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