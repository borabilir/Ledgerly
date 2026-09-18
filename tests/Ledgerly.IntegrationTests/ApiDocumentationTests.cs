using System.Net;
using System.Text.Json;
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

        using var document = await JsonDocument.ParseAsync(
            await openApiResponse.Content.ReadAsStreamAsync()
        );
        var paths = document.RootElement.GetProperty("paths");
        var depositResponses = paths.GetProperty("/api/wallets/{walletId}/test-deposits")
            .GetProperty("post").GetProperty("responses");
        foreach (var status in new[] { "200", "400", "404", "409", "500" })
            Assert.True(depositResponses.TryGetProperty(status, out _));
        var getResponses = paths.GetProperty("/api/wallets/{walletId}")
            .GetProperty("get").GetProperty("responses");
        Assert.True(getResponses.TryGetProperty("200", out _));
        Assert.True(getResponses.TryGetProperty("404", out _));
        Assert.True(paths.GetProperty("/api/wallets").GetProperty("post")
            .GetProperty("responses").TryGetProperty("201", out _));
    }
}
