using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Infrastructure;
using Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace OG.Application.UnitTests.Security;

public class AuthenticationExtensionsTests
{
    private const string AuthorityHttp = "http://localhost:8080/realms/omakase-gateway";
    private const string AuthorityHttps = "https://idp.omakase.local/realms/omakase-gateway";

    /// <summary>Config de Development: Keycloak por HTTP, metadata sin TLS permitida.</summary>
    private static IConfiguration DevConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Keycloak:Authority"] = AuthorityHttp,
            ["Authentication:Keycloak:RequireHttpsMetadata"] = "false",
        })
        .Build();

    private static (JwtBearerOptions Options, ServiceProvider Provider) Build(
        IConfiguration configuration, Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        extra?.Invoke(services);
        services.AddOmakaseAuthentication(configuration);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        return (options, provider);
    }

    /// <summary>Abre un Activity real para poder inspeccionar las etiquetas que fija el handler.</summary>
    private static (Activity Activity, IDisposable Cleanup) StartActivity()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        var source = new ActivitySource("tests.auth");
        var activity = source.StartActivity("auth")!;

        return (activity, new Cleanup(() => { activity.Dispose(); source.Dispose(); listener.Dispose(); }));
    }

    private sealed class Cleanup : IDisposable
    {
        private readonly Action _dispose;
        public Cleanup(Action dispose) => _dispose = dispose;
        public void Dispose() => _dispose();
    }

    private static AuthenticationFailedContext FailedContext(
        JwtBearerOptions options, ServiceProvider provider, Exception exception)
    {
        var http = new DefaultHttpContext { RequestServices = provider };
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");

        return new AuthenticationFailedContext(
            http,
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            options)
        {
            Exception = exception,
        };
    }

    // ── Metadata OIDC sobre TLS (endurecimiento) ──────────────────────────────

    [Fact]
    public void RequireHttpsMetadata_EsTruePorDefecto()
    {
        // Estaba fijo en false. Sin TLS, un atacante en la red sirve su propio documento de
        // descubrimiento —y con él sus propias claves de firma— y el token forjado se acepta.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Keycloak:Authority"] = AuthorityHttps,
            })
            .Build();

        var (options, _) = Build(config);

        Assert.True(options.RequireHttpsMetadata);
    }

    [Fact]
    public void RequireHttpsMetadata_SoloSeDesactivaSiSeDeclaraExplicitamente()
    {
        var (options, _) = Build(DevConfig());

        Assert.False(options.RequireHttpsMetadata);
    }

    // ── Fallo de autenticación: token inválido ≠ Keycloak caído ───────────────

    [Fact]
    public async Task TokenInvalido_NoEscribeEnAuditLogs()
    {
        // Regresión de seguridad. Antes, CADA token inválido escribía una fila síncrona en
        // audit_logs con Verdict=Block y Policy/Anomaly/Risk = 100: puntuaciones que ningún
        // motor calculó. Contaminaba las métricas de HU-021, el explorador de HU-022 y la
        // calibración de HU-035, y daba un amplificador de carga (un INSERT por token basura).
        var db = Substitute.For<OmakaseDbContext>(new DbContextOptions<OmakaseDbContext>());
        var auditLogs = Substitute.For<DbSet<Domain.Entities.AuditLog>>();
        db.AuditLogs.Returns(auditLogs);

        var (options, provider) = Build(DevConfig(), s => s.AddScoped(_ => db));
        var context = FailedContext(options, provider, new SecurityTokenInvalidSignatureException("firma ajena"));

        await options.Events!.OnAuthenticationFailed!(context);

        auditLogs.DidNotReceiveWithAnyArgs().Add(default!);
        await db.DidNotReceiveWithAnyArgs().SaveChangesAsync();
    }

    [Fact]
    public async Task TokenInvalido_NoSeMarcaComoDegradacionDeKeycloak()
    {
        // Un token expirado o manipulado es tráfico hostil normal, no una caída de la dependencia.
        // Marcarlo como tal falseaba la señal de degradación de HU-032.
        var (options, provider) = Build(DevConfig());
        var (activity, cleanup) = StartActivity();
        using (cleanup)
        {
            var context = FailedContext(options, provider, new SecurityTokenExpiredException("expirado"));

            await options.Events!.OnAuthenticationFailed!(context);

            Assert.Null(activity.GetTagItem("dependency.name"));
            Assert.NotEqual(ActivityStatusCode.Error, activity.Status);
        }
    }

    [Fact]
    public async Task FalloDeLaDependencia_SiSeMarcaComoDegradacionDeKeycloak()
    {
        // Lo que sí es una degradación real: no poder descargar la metadata OIDC, timeout,
        // error de transporte. Ahí el span sí debe llevar error=true y el nombre de la dependencia.
        var (options, provider) = Build(DevConfig());
        var (activity, cleanup) = StartActivity();
        using (cleanup)
        {
            var context = FailedContext(options, provider,
                new InvalidOperationException("IDX20803: Unable to obtain configuration from: 'http://localhost:8080/...'"));

            await options.Events!.OnAuthenticationFailed!(context);

            Assert.Equal("Keycloak", activity.GetTagItem("dependency.name"));
            Assert.Equal(true, activity.GetTagItem("error"));
            Assert.Equal(ActivityStatusCode.Error, activity.Status);
        }
    }

    // ── Mapeo de roles del realm ──────────────────────────────────────────────

    [Fact]
    public async Task OnTokenValidated_MapeaLosRolesDeKeycloakAClaims()
    {
        var (options, _) = Build(DevConfig());

        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim("realm_access", JsonSerializer.Serialize(new { roles = new[] { "ADMIN", "VIEWER" } })));

        var context = new TokenValidatedContext(
            new DefaultHttpContext(),
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            options)
        {
            Principal = new ClaimsPrincipal(identity),
        };

        await options.Events!.OnTokenValidated!(context);

        var roles = identity.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.Contains("ADMIN", roles);
        Assert.Contains("VIEWER", roles);
    }
}
