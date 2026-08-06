using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Common.RiskEngine;
using Application.Common.RiskEngine.AnomalyDetection;
using Application.Common.Security.Mfa;
using Domain.Entities;
using Domain.ValueObjects;
using Infrastructure.Persistence.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace OG.Application.UnitTests.Persistence;

/// <summary>
/// Pruebas de los decoradores de caché de la ruta caliente del motor de riesgo (T-072).
/// </summary>
/// <remarks>
/// Cada decorador tiene que cumplir dos contratos: memorizar mientras el TTL esté vigente, y
/// <b>delegar siempre</b> cuando el TTL sea 0. Esa segunda parte es la válvula de seguridad que
/// permite descartar la caché como causa de un comportamiento raro sin recompilar, así que se
/// prueba explícitamente en los tres.
/// </remarks>
public class RiskEngineCacheTests
{
    private static IMemoryCache NewCache() => new MemoryCache(new MemoryCacheOptions());

    private static IOptions<RiskEngineCacheOptions> Ttl(int seconds) =>
        Options.Create(new RiskEngineCacheOptions
        {
            ServicePoliciesTtlSeconds = seconds,
            RiskConfigTtlSeconds = seconds,
            UserMfaInfoTtlSeconds = seconds,
        });

    // ── Políticas por servicio (27,4 % del presupuesto medido) ────────────────

    private static ServicePolicySet Set() =>
        new(ProtectedServiceId.New(), Array.Empty<AccessPolicy>(), RequiresAuth: false);

    [Fact]
    public async Task PoliticasDeServicio_SeConsultanUnaSolaVezMientrasElTtlEsteVigente()
    {
        var inner = Substitute.For<IServicePolicyProvider>();
        inner.GetByServiceNameAsync("httpbin", Arg.Any<CancellationToken>()).Returns(Set());

        var sut = new CachedServicePolicyProvider(inner, NewCache(), Ttl(5));

        for (var i = 0; i < 5; i++)
            Assert.NotNull(await sut.GetByServiceNameAsync("httpbin"));

        await inner.Received(1).GetByServiceNameAsync("httpbin", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PoliticasDeServicio_ServiciosDistintosNoCompartenEntrada()
    {
        var inner = Substitute.For<IServicePolicyProvider>();
        inner.GetByServiceNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Set());

        var sut = new CachedServicePolicyProvider(inner, NewCache(), Ttl(5));

        await sut.GetByServiceNameAsync("httpbin");
        await sut.GetByServiceNameAsync("otro-servicio");

        await inner.Received(1).GetByServiceNameAsync("httpbin", Arg.Any<CancellationToken>());
        await inner.Received(1).GetByServiceNameAsync("otro-servicio", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PoliticasDeServicio_ConTtlCero_DelegaSiempre()
    {
        var inner = Substitute.For<IServicePolicyProvider>();
        inner.GetByServiceNameAsync("httpbin", Arg.Any<CancellationToken>()).Returns(Set());

        var sut = new CachedServicePolicyProvider(inner, NewCache(), Ttl(0));

        for (var i = 0; i < 3; i++)
            await sut.GetByServiceNameAsync("httpbin");

        await inner.Received(3).GetByServiceNameAsync("httpbin", Arg.Any<CancellationToken>());
    }

    // ── Configuración de riesgo (12,9 %) ──────────────────────────────────────

    private static RiskScoreConfig Config() => new()
    {
        Id = RiskScoreConfigId.New(),
        PolicyWeight = 0.5m,
        AnomalyWeight = 0.5m,
        ColdStartPenalty = 30m,
        ColdStartN = 10,
        ChallengeThreshold = 33m,
        BlockThreshold = 70m,
    };

    [Fact]
    public async Task ConfigDeRiesgo_SeConsultaUnaSolaVezMientrasElTtlEsteVigente()
    {
        var inner = Substitute.For<IRiskConfigProvider>();
        inner.GetAsync(Arg.Any<CancellationToken>()).Returns(Config());

        var sut = new CachedRiskConfigProvider(inner, NewCache(), Ttl(5));

        for (var i = 0; i < 5; i++)
            Assert.NotNull(await sut.GetAsync());

        await inner.Received(1).GetAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfigDeRiesgo_ConTtlCero_DelegaSiempre()
    {
        var inner = Substitute.For<IRiskConfigProvider>();
        inner.GetAsync(Arg.Any<CancellationToken>()).Returns(Config());

        var sut = new CachedRiskConfigProvider(inner, NewCache(), Ttl(0));

        for (var i = 0; i < 3; i++)
            await sut.GetAsync();

        await inner.Received(3).GetAsync(Arg.Any<CancellationToken>());
    }

    // ── Estado MFA del usuario (parte del 25,7 % de step-up) ──────────────────

    [Fact]
    public async Task EstadoMfa_SeConsultaUnaSolaVezMientrasElTtlEsteVigente()
    {
        var inner = Substitute.For<IUserMfaInfoProvider>();
        inner.GetAsync("u1", Arg.Any<CancellationToken>()).Returns(new UserMfaInfo(true, true));

        var sut = new CachedUserMfaInfoProvider(inner, NewCache(), Ttl(5));

        for (var i = 0; i < 5; i++)
            Assert.NotNull(await sut.GetAsync("u1"));

        await inner.Received(1).GetAsync("u1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EstadoMfa_ConTtlCero_DelegaSiempre()
    {
        var inner = Substitute.For<IUserMfaInfoProvider>();
        inner.GetAsync("u1", Arg.Any<CancellationToken>()).Returns(new UserMfaInfo(true, true));

        var sut = new CachedUserMfaInfoProvider(inner, NewCache(), Ttl(0));

        for (var i = 0; i < 3; i++)
            await sut.GetAsync("u1");

        await inner.Received(3).GetAsync("u1", Arg.Any<CancellationToken>());
    }

    // ── Perfil: memo por petición (16,8 % + parte del 16,7 %) ─────────────────

    private static UserAnomalyProfile Profile() =>
        new(Array.Empty<AnomalyFeatureVector>(), Array.Empty<UserAccessSample>(), AccessCount: 380, IsColdStart: false);

    [Fact]
    public async Task Perfil_LaSegundaLecturaDeLaMismaPeticionNoVuelveAConsultar()
    {
        // El detector de anomalías y la penalización de cold-start pedían el MISMO perfil por
        // separado dentro de una sola evaluación.
        var inner = Substitute.For<IUserProfileStore>();
        inner.GetAsync("u1", Arg.Any<CancellationToken>()).Returns(Profile());

        var sut = new RequestScopedUserProfileStore(inner);

        var a = await sut.GetAsync("u1");
        var b = await sut.GetAsync("u1");

        Assert.Same(a, b);
        await inner.Received(1).GetAsync("u1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Perfil_TambienMemorizaLaAusenciaDePerfil()
    {
        // Un usuario nuevo (sin perfil) pagaba dos búsquedas fallidas por evaluación.
        var inner = Substitute.For<IUserProfileStore>();
        inner.GetAsync("nuevo", Arg.Any<CancellationToken>()).Returns((UserAnomalyProfile?)null);

        var sut = new RequestScopedUserProfileStore(inner);

        Assert.Null(await sut.GetAsync("nuevo"));
        Assert.Null(await sut.GetAsync("nuevo"));

        await inner.Received(1).GetAsync("nuevo", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Perfil_UsuariosDistintosNoCompartenEntrada()
    {
        var inner = Substitute.For<IUserProfileStore>();
        inner.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Profile());

        var sut = new RequestScopedUserProfileStore(inner);

        await sut.GetAsync("u1");
        await sut.GetAsync("u2");

        await inner.Received(1).GetAsync("u1", Arg.Any<CancellationToken>());
        await inner.Received(1).GetAsync("u2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Perfil_ElMemoNoSobreviveAOtraPeticion()
    {
        // Cada petición resuelve un ámbito nuevo, luego una instancia nueva del decorador: el memo
        // no puede arrastrar datos entre peticiones. Esto es lo que hace que aquí no exista ventana
        // de obsolescencia, a diferencia de las cachés con TTL.
        var inner = Substitute.For<IUserProfileStore>();
        inner.GetAsync("u1", Arg.Any<CancellationToken>()).Returns(Profile());

        await new RequestScopedUserProfileStore(inner).GetAsync("u1");
        await new RequestScopedUserProfileStore(inner).GetAsync("u1");

        await inner.Received(2).GetAsync("u1", Arg.Any<CancellationToken>());
    }
}
