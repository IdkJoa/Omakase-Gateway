using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OG.Gateway.Api;
using OG.IntegrationTests.Fixtures;
using Xunit;

namespace OG.IntegrationTests.E2E;

[Collection("GatewayIntegrationCollection")]
public class RiskEvaluationAndAuditE2ETests
{
    private readonly CustomWebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public RiskEvaluationAndAuditE2ETests(CustomWebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task EvaluateRisk_WhenRequestSent_ShouldAssignVerdictAndPersistAuditLog()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/testservice/get");
        request.Headers.Add("User-Agent", "IntegrationTest-Agent/1.0");
        request.Headers.Add("Accept-Language", "es-ES,es;q=0.9");

        // Act
        var response = await _client.SendAsync(request);

        // Assert - Verificación de la respuesta HTTP del Gateway (200 OK o proxy forwarding)
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable, HttpStatusCode.NotFound);

        // Esperar un momento breve para que el worker en segundo plano procese el canal de auditoría (IAuditChannel)
        await Task.Delay(500);

        // Consultar la base de datos PostgreSQL efímera directamente
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OmakaseDbContext>();

        var auditLog = await db.AuditLogs.FirstOrDefaultAsync(a => a.UserAgent != null && a.UserAgent.Contains("IntegrationTest-Agent"));

        auditLog.Should().NotBeNull("La petición interceptada debe registrarse en la tabla audit_logs de PostgreSQL.");
        auditLog!.Verdict.Should().BeDefined();
        auditLog.FingerprintHash.Should().NotBeNullOrWhiteSpace();
    }
}
