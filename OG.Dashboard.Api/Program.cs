using Infrastructure;
using Infrastructure.Persistence;
using Infrastructure.Security;
using OG.Dashboard;
using OG.Dashboard.Api.Endpoints;
using OG.Dashboard.Api.Extensions;
using ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// ── Aspire ServiceDefaults & Data Resources ──────────────────────────────────
builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<OmakaseDbContext>("Omakase");
builder.AddRedisClient("redis");

// ── 1. Capa de Aplicación (Services & Handlers de OG.Dashboard) ───────────────
builder.Services.AddDashboardApplication();

// ── 2. Capa de API (Swagger, Auth, CORS y Servicios Web de ApiExtensions) ─────
builder.Services.AddDashboardApiConfiguration(builder.Configuration);

var app = builder.Build();

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

app.MapAuditLogsEndpoints();
app.MapMetricsEndpoints();
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
app.MapRiskConfigEndpoints();
app.MapRolesEndpoints();
app.MapServicePoliciesEndpoints();
app.MapServicesEndpoints();
app.MapUsersEndpoints();
app.MapUserProfileEndpoints();

await app.RunAsync();