using Infrastructure;
using Infrastructure.Persistence;
using Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using OG.Dashboard.Api.Endpoints;
using ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<OmakaseDbContext>("Omakase");
builder.AddRedisClient("redis");
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddOmakaseAuthentication(builder.Configuration);
builder.Services.AddAuthorization(options =>
{
    // Escritura: solo ADMIN. Lectura: ADMIN o VIEWER (escenario "Viewer solo lectura"
    // de HU-028). Ambas policies son el seam que T-058 re-implementará contra
    // user_roles sin tocar los endpoints.
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("ADMIN"));
    options.AddPolicy("ReadAccess", policy => policy.RequireRole("ADMIN", "VIEWER"));
});

// HU-023 T-047: puente de identidad Keycloak → tabla users (JIT provisioning).
// Resuelve el claim `sub` del token a la fila local que exigen las FKs
// (access_policies.created_by, user_roles, audit_logs). SRS §9.5.
builder.Services.AddSingleton<Application.Common.Security.ILogSanitizer,
                              Application.Common.Security.LogSanitizer>();
builder.Services.AddSingleton<Application.Common.Security.IOutputSanitizer,
                              Application.Common.Security.HtmlOutputSanitizer>();
builder.Services.AddScoped<Application.Common.Security.ICurrentUserService,
                           Infrastructure.Security.KeycloakCurrentUserService>();

// HU-023 T-047: validadores de config JSONB por tipo de regla (espejo de los
// evaluadores del motor). Stateless → Singleton. Registrar aquí el validador de
// cada tipo de regla nuevo (seam Open/Closed).
builder.Services.AddSingleton<Application.Common.RiskEngine.Rules.Validation.IPolicyConfigValidator,
                              Application.Common.RiskEngine.Rules.Validation.GeofenceConfigValidator>();
builder.Services.AddSingleton<Application.Common.RiskEngine.Rules.Validation.IPolicyConfigValidator,
                              Application.Common.RiskEngine.Rules.Validation.TimeWindowConfigValidator>();

// CORS para que el front Angular (localhost:4200) consuma los mocks (Contract-First).
const string FrontendCors = "FrontendCors";
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCors, policy =>
        policy.WithOrigins("http://localhost:4200", "https://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

//  Infrastructure (repositorios, etc.) — se completa en HU-003/004 
// builder.Services.AddInfrastructure();
builder.Services.AddSingleton<Application.Common.Security.IRedisService, Infrastructure.Redis.RedisService>();

// Register Dashboard handlers
builder.Services.AddScoped<OG.Dashboard.Features.Services.GetProtectedServicesHandler>();
builder.Services.AddScoped<OG.Dashboard.Features.Services.GetProtectedServiceHandler>();
builder.Services.AddScoped<OG.Dashboard.Features.Services.CreateProtectedServiceHandler>();
builder.Services.AddScoped<OG.Dashboard.Features.Services.UpdateProtectedServiceHandler>();
builder.Services.AddScoped<OG.Dashboard.Features.Services.DeleteProtectedServiceHandler>();
builder.Services.AddScoped<OG.Dashboard.Features.Metrics.GetMetricsSummaryHandler>();

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

// Habilitar CORS antes de mapear los endpoints.
app.UseCors(FrontendCors);

app.UseAuthentication();
app.UseRbacAuthorization();   // HU-028 T-058: roles de BD (user_roles) reemplazan los del JWT
app.UseAuthorization();

//  Endpoints de diagnóstico Aspire (/health y /alive)
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "OG Dashboard API v1");
    });
}

// HU-028 T-058: autorización granular por endpoint (ReadAccess para lectura,
// AdminOnly para escritura). Cada endpoint declara su propia policy.
var apiGroup = app.MapGroup("");

apiGroup.MapAuditLogsEndpoints();
apiGroup.MapMetricsEndpoints();
apiGroup.MapServicesEndpoints();
apiGroup.MapUsersEndpoints();
apiGroup.MapRolesEndpoints();

// HU-023 / HU-025 / HU-026: endpoints reales — declaran su propia autorización
// (AdminOnly en mutaciones, ReadAccess en lecturas).
app.MapPoliciesEndpoints();
app.MapServicePoliciesEndpoints();
app.MapRiskConfigEndpoints();
app.MapUserProfileEndpoints();

// HU-047: gestión de MFA (TOTP) por administrador — AdminOnly.
app.MapUserMfaEndpoints();

await app.RunAsync();