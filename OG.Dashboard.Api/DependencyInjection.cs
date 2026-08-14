using Application.Common.Security;
using Infrastructure.Redis;
using Infrastructure.Security;
using OG.Dashboard;

namespace OG.Dashboard.Api;

public static class DependencyInjection
{
    public const string FrontendCorsPolicy = "FrontendCors";

    public static IServiceCollection AddDashboardApiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDashboardApplication();

        services.AddOpenApi();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        services.AddOmakaseAuthentication(configuration);
        services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminOnly", policy => policy.RequireRole("ADMIN"));
            options.AddPolicy("ReadAccess", policy => policy.RequireRole("ADMIN", "VIEWER"));
        });

        services.AddSingleton<ILogSanitizer, LogSanitizer>();
        services.AddSingleton<IOutputSanitizer, HtmlOutputSanitizer>();
        services.AddScoped<ICurrentUserService, KeycloakCurrentUserService>();
        services.AddSingleton<IRedisService, RedisService>();

        services.AddCorsConfiguration();

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
}
