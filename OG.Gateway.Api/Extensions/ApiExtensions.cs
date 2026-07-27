using System.Net;
using Infrastructure.GeoLocation;
using Infrastructure.Proxy;
using Infrastructure.Workers;
using Microsoft.AspNetCore.HttpOverrides;
using OmakaseGateway.Api;
using Yarp.ReverseProxy.Configuration;

namespace OG.Gateway.Api.Extensions;

public static class ApiExtensions
{
    /// <summary>
    /// Registra la configuración referente a la API del Gateway (YARP Reverse Proxy, Forwarded Headers, Autenticación y Workers de API).
    /// </summary>
    public static IServiceCollection AddGatewayApiConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOpenApi();

        // ── YARP Reverse Proxy con Hidratación Dinámica ────────────────────────────
        services.AddReverseProxy();
        services.AddSingleton<DatabaseProxyConfigProvider>();
        services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<DatabaseProxyConfigProvider>());
        services.AddHostedService<ProxyConfigReloader>();
        services.Configure<ProxyReloadOptions>(configuration.GetSection(ProxyReloadOptions.SectionName));

        // ── Workers de Persistencia de Auditoría de Infraestructura ──────────────
        services.AddHostedService<AuditPersistenceWorker>();
        services.Configure<AuditWorkerOptions>(configuration.GetSection(AuditWorkerOptions.SectionName));
        services.Configure<GeoLocationOptions>(configuration.GetSection(GeoLocationOptions.SectionName));

        // ── Autenticación HTTP de Gateway ────────────────────────────────────────
        services.AddGatewayAuthentication();

        // ── Forwarded Headers (Proxy IP Resolution) ─────────────────────────────
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
