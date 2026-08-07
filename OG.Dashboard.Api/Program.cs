using Infrastructure;
using Infrastructure.Persistence;
using Infrastructure.Security;
using OG.Dashboard;
using OG.Dashboard.Api.Endpoints;
using OG.Dashboard.Api.Extensions;
using ServiceDefaults;

namespace OG.Dashboard.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // ── Aspire ServiceDefaults & Data Resources ──────────────────────────────────
        builder.AddServiceDefaults();
        builder.AddNpgsqlDbContext<OmakaseDbContext>("Omakase");
        builder.AddRedisClient("redis");

        // ── 1. Capa de Aplicación (Services & Handlers de OG.Dashboard) ───────────────
        builder.Services.AddDashboardApplication();

        // ── 2. Capa de API (Swagger, Auth, CORS y Servicios Web de ApiExtensions) ─────
        builder.Services.AddDashboardApiConfiguration(builder.Configuration);

        // HU-047 T-108: gestión de MFA (TOTP) desde el Dashboard. Reutiliza los primitivos de HU-046
        // (ITotpService/ITotpSecretProtector); la clave AES se comparte con el Gateway vía la sección
        // Mfa (mismo secreto en reposo → un secreto enrolado aquí lo verifica el Gateway). Puros/sin
        // estado mutable → Singleton; el servicio de administración → Scoped (misma vida que el request).
        builder.Services.Configure<Application.Common.Security.Mfa.MfaOptions>(
            builder.Configuration.GetSection(Application.Common.Security.Mfa.MfaOptions.SectionName));
        builder.Services.AddSingleton<Application.Common.Security.Mfa.ITotpService,
                                      Application.Common.Security.Mfa.TotpService>();
        builder.Services.AddSingleton<Application.Common.Security.Mfa.ITotpSecretProtector,
                                      Infrastructure.Security.AesGcmTotpSecretProtector>();
        builder.Services.AddScoped<OG.Dashboard.Features.Mfa.MfaAdminService>();

        var app = builder.Build();

        app.UseMiddleware<SecurityHeadersMiddleware>();

        // ── Middleware Pipeline ────────────────────────────────────────────────────────
        app.UseCors(ApiExtensions.FrontendCorsPolicy);
        app.UseAuthentication();
        app.UseRbacAuthorization();
        app.UseAuthorization();

        app.MapDefaultEndpoints();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "OG Dashboard API v1"));
        }

        // HU-028 T-058: autorización granular por endpoint (ReadAccess para lectura,
        // AdminOnly para escritura). Cada endpoint declara su propia policy.
        // NOTA: cada endpoint se registra UNA sola vez. El grupo tiene prefijo vacío y sin
        // metadata, por lo que registrar además en `app` producía rutas duplicadas y rompía el
        // arranque (InvalidOperationException "Duplicate endpoint name"). Se conserva una sola
        // registración por endpoint (estructura previa al merge de rendimiento).
        var apiGroup = app.MapGroup("");

        apiGroup.MapAuditLogsEndpoints();
        apiGroup.MapMetricsEndpoints();
        apiGroup.MapServicesEndpoints();
        apiGroup.MapUsersEndpoints();
        apiGroup.MapRolesEndpoints();

        // HU-023 / HU-025 / HU-026: endpoints reales — declaran su propia autorización
        // (AdminOnly en mutaciones, ReadAccess en lecturas).
        app.MapPoliciesEndpoints();
        app.MapRiskConfigEndpoints();
        app.MapServicePoliciesEndpoints();
        app.MapUserProfileEndpoints();

        // HU-047: gestión de MFA (TOTP) por administrador — AdminOnly.
        app.MapUserMfaEndpoints();

        await app.RunAsync();
    }
}