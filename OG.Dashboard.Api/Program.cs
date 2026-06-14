using Infrastructure;
using Microsoft.EntityFrameworkCore;
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

await app.RunAsync();