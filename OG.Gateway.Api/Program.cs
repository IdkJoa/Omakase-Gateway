using Application.Middlewares;
using Infrastructure;
using Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using OmakaseGateway.Api.Endpoints;
using ServiceDefaults;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// ── Aspire ServiceDefaults ────────────────────────────────────────────────────
// Registra OpenTelemetry (traces + metrics + logs), health checks y
// service discovery automático entre recursos del AppHost.
builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<OmakaseDbContext>("Omakase");
builder.AddRedisClient("redis");
builder.Services.AddOpenApi();

builder.Services.AddInfrastructure();

// T-014 (HU-008) + HU-009 (T-017/T-018): YARP con rutas hidratadas desde protected_services.
// La config ya no sale de appsettings: DatabaseProxyConfigProvider la construye desde la BD y
// ProxyConfigReloader la recarga por polling (sin reiniciar el proceso).
builder.Services.AddReverseProxy();
builder.Services.AddSingleton<Infrastructure.Proxy.DatabaseProxyConfigProvider>();
builder.Services.AddSingleton<Yarp.ReverseProxy.Configuration.IProxyConfigProvider>(
    sp => sp.GetRequiredService<Infrastructure.Proxy.DatabaseProxyConfigProvider>());
builder.Services.AddHostedService<Infrastructure.Proxy.ProxyConfigReloader>();
builder.Services.Configure<Infrastructure.Proxy.ProxyReloadOptions>(
    builder.Configuration.GetSection(Infrastructure.Proxy.ProxyReloadOptions.SectionName));

// T-015: Mediador lightweight propio (sin dependencias externas).
builder.Services.AddScoped<Application.Common.Mediator.IMediator, Application.Common.Mediator.Mediator>();
builder.Services.AddScoped<
    Application.Common.Mediator.IRequestHandler<
        Application.Common.RiskEngine.Commands.EvaluateRiskCommand,
        Application.Common.RiskEngine.Commands.RiskEvaluationResult>,
    Application.Common.RiskEngine.Commands.EvaluateRiskHandler>();

// T-015: Sanitizador de logs/audit (Singleton: sin estado mutable).
builder.Services.AddSingleton<Application.Common.Security.ILogSanitizer, Application.Common.Security.LogSanitizer>();

// T-016: Canal de auditoría asíncrono (System.Threading.Channels, bounded 10 000 items).
// El BackgroundService AuditPersistenceWorker (T-100) drena el canal y persiste en PostgreSQL.
builder.Services.AddSingleton<Application.Common.Audit.IAuditChannel, Application.Common.Audit.InMemoryAuditChannel>();

// T-100: Worker de persistencia de auditoría.
// Consume IAuditChannel en segundo plano y persiste en audit_logs sin bloquear el pipeline HTTP.
builder.Services.AddHostedService<Infrastructure.Workers.AuditPersistenceWorker>();

// Options Pattern — configuración tipada para los componentes del pipeline.
builder.Services.Configure<Application.Common.Audit.AuditChannelOptions>(
    builder.Configuration.GetSection(Application.Common.Audit.AuditChannelOptions.SectionName));
builder.Services.Configure<Infrastructure.Workers.AuditWorkerOptions>(
    builder.Configuration.GetSection(Infrastructure.Workers.AuditWorkerOptions.SectionName));
builder.Services.Configure<Application.Common.Options.RateLimitingOptions>(
    builder.Configuration.GetSection(Application.Common.Options.RateLimitingOptions.SectionName));
builder.Services.Configure<Infrastructure.GeoLocation.GeoLocationOptions>(
    builder.Configuration.GetSection(Infrastructure.GeoLocation.GeoLocationOptions.SectionName));

// HU-019 / T-038: Opciones del JWT propio del Gateway.
// La SecretKey la inyecta Azure Key Vault en producción; en desarrollo proviene de appsettings/user-secrets.
builder.Services.Configure<Application.Common.Options.JwtOptions>(
    builder.Configuration.GetSection(Application.Common.Options.JwtOptions.SectionName));

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
                if (IPAddress.TryParse(proxy, out var ip))
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
app.UseMiddleware<RateLimitMiddleware>();

// T-015: Interceptar cada petición para extracción de contexto y evaluación de riesgo.
app.UseMiddleware<RiskEvaluationMiddleware>();

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

// HU-019 / T-038: Endpoints de autenticación de Client Users.
// Se registran ANTES de MapReverseProxy para que YARP no intercepte /auth/*.
app.MapAuthEndpoints();

app.MapReverseProxy();

await app.RunAsync();