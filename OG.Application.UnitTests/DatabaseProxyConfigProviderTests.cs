using System.Collections.Generic;
using System.Linq;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure.Proxy;
using Xunit;

namespace OG.Application.UnitTests;

// DatabaseProxyConfigProvider (HU-009 / T-017, T-018): mapeo de protected_services a rutas/clusters YARP y recarga vía change token.
public class DatabaseProxyConfigProviderTests
{
    private static ProtectedService Service(string name, string url, bool active = true) => new()
    {
        Id = ProtectedServiceId.New(),
        Name = name,
        UpstreamUrl = url,
        RequiresAuth = false,
        IsActive = active,
    };

    // T-017 — cada servicio activo se mapea a un cluster con su upstream_url y su ruta.
    [Fact]
    public void Update_MapsServiceToRouteAndCluster()
    {
        var sut = new DatabaseProxyConfigProvider();

        sut.Update(new List<ProtectedService> { Service("httpbin", "https://httpbin.org/") });

        var config = sut.GetConfig();
        var route = Assert.Single(config.Routes);
        var cluster = Assert.Single(config.Clusters);

        Assert.Equal("route-httpbin", route.RouteId);
        Assert.Equal("httpbin", route.ClusterId);
        Assert.Equal("/httpbin/{**catch-all}", route.Match.Path);

        Assert.Equal("httpbin", cluster.ClusterId);
        Assert.Equal("https://httpbin.org/", cluster.Destinations!["primary"].Address);
    }

    [Fact]
    public void Update_WithMultipleServices_ProducesOneRoutePerService()
    {
        var sut = new DatabaseProxyConfigProvider();

        sut.Update(new List<ProtectedService>
        {
            Service("nominas", "http://nominas:8080/"),
            Service("reportes", "http://reportes:8080/"),
        });

        var config = sut.GetConfig();
        Assert.Equal(2, config.Routes.Count);
        Assert.Equal(2, config.Clusters.Count);
        Assert.Contains(config.Clusters, c => c.ClusterId == "nominas");
        Assert.Contains(config.Clusters, c => c.ClusterId == "reportes");
    }

    [Fact]
    public void InitialConfig_IsEmpty()
    {
        var sut = new DatabaseProxyConfigProvider();

        var config = sut.GetConfig();

        Assert.Empty(config.Routes);
        Assert.Empty(config.Clusters);
    }

    // T-018 — la recarga señala el change token anterior y expone la nueva config.
    [Fact]
    public void Update_SignalsPreviousChangeToken_AndSwapsConfig()
    {
        var sut = new DatabaseProxyConfigProvider();
        sut.Update(new List<ProtectedService> { Service("v1", "http://v1/") });

        var previous = sut.GetConfig();
        Assert.False(previous.ChangeToken.HasChanged);

        // Cambio de upstream (simula edición en BD) -> nueva recarga.
        sut.Update(new List<ProtectedService> { Service("v1", "http://v2/") });

        // El token de la config anterior queda señalado (YARP relee).
        Assert.True(previous.ChangeToken.HasChanged);

        var current = sut.GetConfig();
        Assert.NotSame(previous, current);
        Assert.Equal("http://v2/", current.Clusters.Single().Destinations!["primary"].Address);
    }
}
