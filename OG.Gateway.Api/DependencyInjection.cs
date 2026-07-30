using System.Net;
using Application;
using Infrastructure;
using Infrastructure.GeoLocation;
using Infrastructure.Proxy;
using Infrastructure.Workers;
using Microsoft.AspNetCore.HttpOverrides;
using Yarp.ReverseProxy.Configuration;

namespace OmakaseGateway.Api;

public static class DependencyInjection
{
    /// <summary>
    /// Registra los servicios de la API del Gateway (YARP Reverse Proxy, Middlewares HTTP, Autenticación)
    /// e invoca AddGatewayApplication() y AddInfrastructure().
    /// </summary>
    public static IServiceCollection AddGatewayApiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOpenApi();

        // ── Capa de Aplicación (OG.Gateway) & Capa de Infraestructura 
        services.AddGatewayApplication(configuration);
        services.AddInfrastructure();

        // ── YARP Reverse Proxy con Hidratación Dinámica 
        services.AddReverseProxy();
        services.AddSingleton<DatabaseProxyConfigProvider>();
        services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<DatabaseProxyConfigProvider>());
        services.AddHostedService<ProxyConfigReloader>();
        services.Configure<ProxyReloadOptions>(configuration.GetSection(ProxyReloadOptions.SectionName));

        // ── Workers de Persistencia de Infraestructura
        services.AddHostedService<AuditPersistenceWorker>();
        services.Configure<AuditWorkerOptions>(configuration.GetSection(AuditWorkerOptions.SectionName));
        services.Configure<GeoLocationOptions>(configuration.GetSection(GeoLocationOptions.SectionName));

        // ── Autenticación HTTP & Key Vault
        services.AddGatewayAuthentication();

        // ── Forwarded Headers (Proxy IP Resolution)
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            var configSection = configuration.GetSection("ForwardedHeaders");
            var trustAll = configSection.GetValue<bool>("TrustAll", true);

            if (trustAll)
            {
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            }
            else
            {
                var knownProxies = configSection.GetSection("KnownProxies").Get<string[]>();
                if (knownProxies != null)
                {
                    foreach (var proxy in knownProxies)
                    {
                        if (IPAddress.TryParse(proxy, out var ip))
                        {
                            options.KnownProxies.Add(ip);
                        }
                    }
                }
            }
        });

        return services;
    }
}
