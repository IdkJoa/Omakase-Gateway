using Infrastructure;
using Infrastructure.Persistence;
using Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using OG.Dashboard.Api.Endpoints;
using ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<OmakaseDbContext>("Omakase");
builder.AddRedisClient("redis");
builder.Services.AddOpenApi();

builder.Services.AddOmakaseAuthentication(builder.Configuration);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("ADMIN"));
});

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

var app = builder.Build();

// Habilitar CORS antes de mapear los endpoints.
app.UseCors(FrontendCors);

app.UseAuthentication();
app.UseAuthorization();

//  Endpoints de diagnóstico Aspire (/health y /alive)
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// T-088: Contratos de API mock (Contract-First)
var apiGroup = app.MapGroup("").RequireAuthorization("AdminOnly");

apiGroup.MapAuditLogsEndpoints();
apiGroup.MapMetricsEndpoints();
apiGroup.MapPoliciesEndpoints();
apiGroup.MapServicesEndpoints();
apiGroup.MapUsersEndpoints();
apiGroup.MapRolesEndpoints();
apiGroup.MapRiskConfigEndpoints();

await app.RunAsync();