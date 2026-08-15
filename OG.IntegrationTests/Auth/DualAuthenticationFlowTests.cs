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

namespace OG.IntegrationTests.Auth;

[Collection("GatewayIntegrationCollection")]
public class DualAuthenticationFlowTests
{
    private readonly CustomWebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public DualAuthenticationFlowTests(CustomWebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ProtectedService_WithRequiresAuthTrue_WithoutJwt_ShouldReturn401Unauthorized()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OmakaseDbContext>();
            var service = await db.ProtectedServices.FirstOrDefaultAsync(s => s.Name == "auth-service");
            if (service == null)
            {
                db.ProtectedServices.Add(new Domain.Entities.ProtectedService
                {
                    Id = Domain.ValueObjects.ProtectedServiceId.From(Guid.NewGuid()),
                    Name = "auth-service",
                    UpstreamUrl = "https://httpbin.org",
                    IsActive = true,
                    RequiresAuth = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();
            }
        }

        var response = await _client.GetAsync("/auth-service/headers");

        // SRS §7.5 / HU-024: sin JWT debe responder 401 (AUTHENTICATION_REQUIRED).
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
