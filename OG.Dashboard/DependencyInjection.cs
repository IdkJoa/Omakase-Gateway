using Application.Common.RiskEngine.Rules.Validation;
using Microsoft.Extensions.DependencyInjection;
using OG.Dashboard.Features.Metrics;
using OG.Dashboard.Features.Services;

namespace OG.Dashboard;

public static class DependencyInjection
{
    public static IServiceCollection AddDashboardApplication(this IServiceCollection services)
    {
        services.AddScoped<GetProtectedServicesHandler>();
        services.AddScoped<GetProtectedServiceHandler>();
        services.AddScoped<CreateProtectedServiceHandler>();
        services.AddScoped<UpdateProtectedServiceHandler>();
        services.AddScoped<DeleteProtectedServiceHandler>();
        services.AddScoped<GetMetricsSummaryHandler>();

        // Validadores de Configuración de Reglas (Open/Closed Seam)
        services.AddSingleton<IPolicyConfigValidator, GeofenceConfigValidator>();
        services.AddSingleton<IPolicyConfigValidator, TimeWindowConfigValidator>();

        return services;
    }
}
