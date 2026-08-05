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

            // Seguro por defecto: si la clave falta, NO se confia en la cabecera. El default anterior
            // era true, asi que una configuracion incompleta dejaba al Gateway aceptando cualquier
            // X-Forwarded-For — un atacante falsificaba su IP y burlaba geofencing, viaje imposible
            // y el rate-limit por IP. Development lo activa explicitamente para simular ataques (HU-034).
            //
            // Semantica verificada en ForwardedHeadersHardeningTests:
            //  - TrustAll=true vacia ambas listas y ASP.NET entonces OMITE la comprobacion de proxy
            //    conocido, es decir acepta la cabecera de cualquier cliente.
            //  - TrustAll=false conserva los defaults del framework (loopback) y descarta la cabecera
            //    de cualquier cliente remoto.
            // RIESGO RESIDUAL DECLARADO: con TrustAll=false, un proceso en la MISMA maquina sigue
            // pudiendo falsificar la IP, porque ASP.NET confia en loopback por defecto. No se limpian
            // esas listas aqui a proposito: dejarlas vacias sin KnownProxies declarados haria que el
            // framework aceptara la cabecera de todo el mundo, que es justo lo contrario.
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
