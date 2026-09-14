using System.Net;
using Ledgerly.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ledgerly.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ApiDocumentationTests
{
    private readonly HttpClient _client;

    public ApiDocumentationTests(LedgerlyApiFactory factory)
    {
        _client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            }
        );
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DocumentationEndpoints_ShouldBeAvailable()
    {
        // Act
        var openApiResponse = await _client.GetAsync("/openapi/v1.json");
        var scalarResponse = await _client.GetAsync("/scalar/v1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, openApiResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, scalarResponse.StatusCode);
    }
}
