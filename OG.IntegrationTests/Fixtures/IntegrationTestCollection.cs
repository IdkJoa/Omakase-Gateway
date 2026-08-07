using OG.Gateway.Api;
using Xunit;

namespace OG.IntegrationTests.Fixtures;

[CollectionDefinition("GatewayIntegrationCollection")]
public class GatewayIntegrationCollection : ICollectionFixture<CustomWebApplicationFactory<Program>>
{
    // Esta clase sirve únicamente como ancla de la colección de xUnit
}
