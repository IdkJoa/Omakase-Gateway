using Application.Common.Security;
using Infrastructure.Redis;
using Infrastructure.Security;

namespace OG.Dashboard.Api.Extensions;

public static class ApiExtensions
{
    public const string FrontendCorsPolicy = "FrontendCors";

    public static IServiceCollection AddDashboardApiConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.ConfigureSwagger();
        services.AddAuth(configuration);
        services.AddCorsConfiguration();

        services.AddSingleton<ILogSanitizer, LogSanitizer>();
        services.AddSingleton<IOutputSanitizer, HtmlOutputSanitizer>();
        services.AddScoped<ICurrentUserService, KeycloakCurrentUserService>();
        services.AddSingleton<IRedisService, RedisService>();

        return services;
    }

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

    private static void ConfigureSwagger(this IServiceCollection services)
    {
        services.AddOpenApi();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
    }

    private static void AddAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOmakaseAuthentication(configuration);
        services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminOnly", policy => policy.RequireRole("ADMIN"));
            options.AddPolicy("ReadAccess", policy => policy.RequireRole("ADMIN", "VIEWER"));
        });
    }
}