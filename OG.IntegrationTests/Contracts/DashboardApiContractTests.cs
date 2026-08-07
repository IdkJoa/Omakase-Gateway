using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using OG.Dashboard.Api;
using OG.IntegrationTests.Fixtures;
using Xunit;

namespace OG.IntegrationTests.Contracts;

public class DashboardApiContractTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public DashboardApiContractTests(CustomWebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/api/v1/services")]
    [InlineData("/api/v1/policies")]
    [InlineData("/api/v1/logs")]
    [InlineData("/api/v1/metrics/summary")]
    [InlineData("/api/v1/users")]
    [InlineData("/api/v1/risk-config")]
    public async Task DashboardApiEndpoints_ShouldRespondWithValidHttpStatusAndJsonSchema(string endpoint)
    {
        // Act
        var response = await _client.GetAsync(endpoint);

        // Assert - SRS §4: Los endpoints del Dashboard deben responder con 200 OK o 401/403 según requiera auth
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

            var contentString = await response.Content.ReadAsStringAsync();
            contentString.Should().NotBeNullOrWhiteSpace();

            // Validar que la respuesta sea un documento JSON válido
            using var jsonDoc = JsonDocument.Parse(contentString);
            jsonDoc.RootElement.ValueKind.Should().BeOneOf(JsonValueKind.Array, JsonValueKind.Object);
        }
    }
}
