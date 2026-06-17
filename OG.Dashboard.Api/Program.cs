using Infrastructure;
using Microsoft.EntityFrameworkCore;
using OG.Dashboard.Api.Endpoints;
using ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<OmakaseDbContext>("Omakase");
builder.AddRedisClient("redis");
builder.Services.AddOpenApi();

//  Infrastructure (repositorios, etc.) — se completa en HU-003/004 
// builder.Services.AddInfrastructure();

var app = builder.Build();

//  Endpoints de diagnóstico Aspire (/health y /alive) 
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// T-088: Contratos de API mock (Contract-First)
app.MapAuditLogsEndpoints();
app.MapMetricsEndpoints();
app.MapPoliciesEndpoints();
app.MapServicesEndpoints();
app.MapUsersEndpoints();
app.MapRolesEndpoints();
app.MapRiskConfigEndpoints();

await app.RunAsync();