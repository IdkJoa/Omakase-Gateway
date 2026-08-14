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

        builder.AddServiceDefaults();
        builder.AddNpgsqlDbContext<OmakaseDbContext>("Omakase");
        builder.AddRedisClient("redis");

        builder.Services.AddDashboardApplication();
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

        // El grupo tiene prefijo vacío y sin metadata: registrar el mismo endpoint también en
        // `app` producía rutas duplicadas y rompía el arranque (Duplicate endpoint name).
        var apiGroup = app.MapGroup("");

        apiGroup.MapAuditLogsEndpoints();
        apiGroup.MapMetricsEndpoints();
        apiGroup.MapServicesEndpoints();
        apiGroup.MapUsersEndpoints();
        apiGroup.MapRolesEndpoints();

        app.MapPoliciesEndpoints();
        app.MapRiskConfigEndpoints();
        app.MapServicePoliciesEndpoints();
        app.MapUserProfileEndpoints();
        app.MapUserMfaEndpoints();

        await app.RunAsync();
    }
}