using Application.Common.Security;
using Infrastructure.Redis;
using Infrastructure.Security;
using OG.Dashboard;

namespace OG.Dashboard.Api;

public static class DependencyInjection
{
    public const string FrontendCorsPolicy = "FrontendCors";

    /// <summary>
    /// Registra los servicios Web/API (Swagger, CORS, Autenticación HTTP, Middlewares) e invoca AddDashboardApplication().
    /// </summary>
    public static IServiceCollection AddDashboardApiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Capa de Aplicación (OG.Dashboard) ──────────────────────────────────
        services.AddDashboardApplication();

        // ── OpenAPI & Swagger (Presentación API) ─────────────────────────────────
        services.AddOpenApi();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        // ── Autenticación & Autorización HTTP (Presentación API) ────────────────
        services.AddOmakaseAuthentication(configuration);
        services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminOnly", policy => policy.RequireRole("ADMIN"));
            options.AddPolicy("ReadAccess", policy => policy.RequireRole("ADMIN", "VIEWER"));
        });

        // ── Servicios de Contexto Web / HTTP ─────────────────────────────────────
        services.AddSingleton<ILogSanitizer, LogSanitizer>();
        services.AddSingleton<IOutputSanitizer, HtmlOutputSanitizer>();
        services.AddScoped<ICurrentUserService, KeycloakCurrentUserService>();
        services.AddSingleton<IRedisService, RedisService>();

        // ── Configuración CORS (Presentación API) ─────────────────────────────────
        services.AddCorsConfiguration();

        return services;
    }

    /// <summary>
    /// Registra la política de CORS para el Frontend Angular.
    /// </summary>
    public static IServiceCollection AddCorsConfiguration(this IServiceCollection services)
    {
        return services.AddCors(options =>
        {
            options.AddPolicy(FrontendCorsPolicy, policy =>
                policy.WithOrigins("http://localhost:4200", "https://localhost:4200")
                      .AllowAnyHeader()
                      .AllowAnyMethod());
        });
    }
}
