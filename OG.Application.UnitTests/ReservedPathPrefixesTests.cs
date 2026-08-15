using System;
using System.Linq;
using Domain.Entities;
using Xunit;

namespace OG.Application.UnitTests;

/// <summary>
/// Protege el invariante entre las dos caras de la misma regla: los nombres que el CRUD
/// administrativo rechaza para un servicio protegido y las rutas que el motor de riesgo deja
/// pasar sin evaluar.
/// <para>
/// Antes eran dos listas escritas a mano en sitios distintos. Que divergieran tenía dos
/// consecuencias, ambas malas: un prefijo reservado sin su equivalente en el motor haría que
/// el Gateway evaluara su propio endpoint de autenticación, donde todavía no hay identidad; y
/// un prefijo exento que no estuviera reservado permitiría registrar un servicio protegido
/// que nunca llegaría a evaluarse ni a proxearse.
/// </para>
/// </summary>
public class ReservedPathPrefixesTests
{
    [Fact]
    public void CadaNombreReservadoTieneSuPrefijoDeRuta()
    {
        var esperados = ProtectedService.ReservedNames
            .Select(n => "/" + n)
            .OrderBy(p => p, StringComparer.Ordinal);

        var reales = ProtectedService.ReservedPathPrefixes
            .OrderBy(p => p, StringComparer.Ordinal);

        Assert.Equal(esperados, reales);
    }

    [Fact]
    public void NoHayPrefijosDeRutaSinNombreReservado()
    {
        Assert.All(
            ProtectedService.ReservedPathPrefixes,
            prefijo => Assert.Contains(prefijo.TrimStart('/'), ProtectedService.ReservedNames));
    }

    [Theory]
    [InlineData("auth")]      // endpoints de identidad: se evalúan antes de tener identidad
    [InlineData("health")]    // sondas de Aspire
    [InlineData("alive")]
    [InlineData("openapi")]
    [InlineData("demo")]      // cliente de demostración de HU-048
    public void LosNombresDelGatewayEstanReservados(string nombre)
    {
        Assert.Contains(nombre, ProtectedService.ReservedNames);
    }

    [Fact]
    public void LaComparacionDeNombresIgnoraMayusculas()
    {
        // El nombre viaja en la URL, donde la caja no distingue: 'Demo' debe quedar
        // reservado igual que 'demo', o el CRUD dejaría colar un servicio que eclipsa la ruta.
        Assert.Contains("DEMO", ProtectedService.ReservedNames);
        Assert.Contains("Auth", ProtectedService.ReservedNames);
    }
}
