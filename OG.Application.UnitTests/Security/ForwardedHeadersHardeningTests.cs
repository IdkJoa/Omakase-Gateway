using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace OG.Application.UnitTests.Security;

/// <summary>
/// Determina el comportamiento REAL de <see cref="ForwardedHeadersMiddleware"/> bajo las dos ramas de
/// configuración del Gateway, porque de ello depende que un atacante pueda o no falsificar su IP de
/// origen y con ello burlar geofencing, viaje imposible y el rate-limit por IP.
/// <para>
/// Contexto: una prueba manual desde localhost con <c>TrustAll=false</c> mostró que la cabecera SÍ se
/// aplicaba. Estos tests separan si eso ocurre para cualquier cliente (fallo de seguridad) o solo desde
/// loopback (comportamiento por defecto de ASP.NET, que confía en la máquina local).
/// </para>
/// </summary>
public class ForwardedHeadersHardeningTests
{
    private readonly ITestOutputHelper _output;

    public ForwardedHeadersHardeningTests(ITestOutputHelper output) => _output = output;

    /// <summary>Réplica exacta de la rama TrustAll=true de ApiExtensions: se vacían ambas listas.</summary>
    private static ForwardedHeadersOptions TrustAll()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        return options;
    }

    /// <summary>Réplica de la rama TrustAll=false SIN KnownProxies declarados: se dejan los defaults.</summary>
    private static ForwardedHeadersOptions SoloProxiesDeclarados()
    {
        return new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        };
    }

    /// <summary>Ejecuta el middleware y devuelve la IP que queda tras procesar la cabecera.</summary>
    private static async Task<string?> IpResultante(
        ForwardedHeadersOptions options, IPAddress remoteIp, string forwardedFor)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteIp;
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;

        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            NullLoggerFactory.Instance,
            Options.Create(options));

        await middleware.Invoke(context);
        return context.Connection.RemoteIpAddress?.ToString();
    }

    [Fact]
    public async Task TrustAll_AceptaLaCabeceraDeCualquierCliente()
    {
        var ip = await IpResultante(TrustAll(), IPAddress.Parse("203.0.113.5"), "133.11.1.1");

        _output.WriteLine($"TrustAll=true, cliente remoto 203.0.113.5 -> {ip}");
        Assert.Equal("133.11.1.1", ip);
    }

    /// <summary>
    /// EL TEST QUE IMPORTA: un atacante remoto NO debe poder falsificar su IP cuando TrustAll=false.
    /// </summary>
    [Fact]
    public async Task SinTrustAll_UnClienteREMOTO_NoPuedeFalsificarSuIp()
    {
        var ip = await IpResultante(SoloProxiesDeclarados(), IPAddress.Parse("203.0.113.5"), "133.11.1.1");

        _output.WriteLine($"TrustAll=false, cliente remoto 203.0.113.5 -> {ip}");
        Assert.Equal("203.0.113.5", ip);
    }

    /// <summary>
    /// Documenta el motivo por el que la prueba manual desde localhost "falló": ASP.NET confía en
    /// loopback por defecto (KnownIPNetworks/KnownProxies incluyen ::1). No es un fallo del
    /// endurecimiento, pero SÍ significa que cualquier proceso de la misma máquina puede falsificar
    /// la IP — hay que declararlo en el modelo de amenazas.
    /// </summary>
    [Fact]
    public async Task SinTrustAll_DesdeLoopback_LaCabeceraSIGUESiendoAceptada()
    {
        var ip = await IpResultante(SoloProxiesDeclarados(), IPAddress.IPv6Loopback, "133.11.1.1");

        _output.WriteLine($"TrustAll=false, cliente loopback ::1 -> {ip}");
        Assert.Equal("133.11.1.1", ip);
    }
}
