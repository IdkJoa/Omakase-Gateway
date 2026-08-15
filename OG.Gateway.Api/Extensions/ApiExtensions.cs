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
    public const string FrontendCorsPolicy = "FrontendCors";

    /// <summary>
    /// Registra la configuración referente a la API del Gateway (YARP Reverse Proxy, Forwarded Headers, Autenticación y Workers de API).
    /// </summary>
    public static IServiceCollection AddGatewayApiConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOpenApi();
        services.AddCorsConfiguration();

        services.AddReverseProxy();
        services.AddSingleton<DatabaseProxyConfigProvider>();
        services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<DatabaseProxyConfigProvider>());
        services.AddHostedService<ProxyConfigReloader>();
        services.Configure<ProxyReloadOptions>(configuration.GetSection(ProxyReloadOptions.SectionName));

        services.AddHostedService<AuditPersistenceWorker>();
        services.Configure<AuditWorkerOptions>(configuration.GetSection(AuditWorkerOptions.SectionName));
        services.Configure<GeoLocationOptions>(configuration.GetSection(GeoLocationOptions.SectionName));

        services.AddGatewayAuthentication();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            var configSection = configuration.GetSection("ForwardedHeaders");

            // Seguro por defecto: si la clave falta, NO se confía en la cabecera X-Forwarded-For (antes
            // el default era true, así que una config incompleta dejaba al Gateway aceptando cualquier
            // IP falsificada y burlaba geofencing/viaje-imposible/rate-limit). Development la activa
            // explícitamente para simular ataques (HU-034).
            // TrustAll=true vacía las listas y ASP.NET omite la comprobación de proxy conocido (acepta
            // la cabecera de cualquiera). TrustAll=false conserva los defaults del framework (loopback);
            // riesgo residual declarado: un proceso en la MISMA máquina aún puede falsificar la IP, pero
            // no se limpian las listas a propósito, porque vaciarlas sin KnownProxies haría lo contrario.
            var trustAll = configSection.GetValue<bool>("TrustAll", false);

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

    /// <summary>
    /// Registra la política de CORS para el Frontend.
    /// </summary>
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
