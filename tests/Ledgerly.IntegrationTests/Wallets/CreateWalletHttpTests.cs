using System.Net;
using System.Net.Http.Json;
using Ledgerly.Api.Contracts.Wallets;
using Ledgerly.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ledgerly.IntegrationTests.Wallets;

[Collection(PostgresCollection.Name)]
public sealed class CreateWalletHttpTests
{
    private readonly LedgerlyApiFactory _factory;
    private readonly HttpClient _client;

    public CreateWalletHttpTests(LedgerlyApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            }
        );
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Create_WhenRequestIsValid_ShouldReturnCreatedAndPersistWallet()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var request = new CreateWalletRequest(ownerId, "try");

        try
        {
            // Act
            var response = await _client.PostAsJsonAsync("/api/wallets", request);
            var body = await response.Content.ReadFromJsonAsync<CreateWalletResponse>();

            // Assert
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.NotNull(body);
            Assert.NotEqual(Guid.Empty, body.WalletId);
            Assert.True(await _factory.WalletExistsAsync(body.WalletId));
        }
        finally
        {
            await _factory.DeleteWalletsAsync(ownerId);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Create_WhenWalletAlreadyExists_ShouldReturnConflict()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var request = new CreateWalletRequest(ownerId, "TRY");

        try
        {
            await _client.PostAsJsonAsync("/api/wallets", request);

            // Act
            var response = await _client.PostAsJsonAsync("/api/wallets", request);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

            // Assert
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.NotNull(problem);
            Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
            Assert.Equal("Wallet already exists", problem.Title);
        }
        finally
        {
            await _factory.DeleteWalletsAsync(ownerId);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Create_WhenOwnerIdIsEmpty_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new CreateWalletRequest(Guid.Empty, "TRY");

        // Act
        var response = await _client.PostAsJsonAsync("/api/wallets", request);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("Invalid request", problem.Title);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Create_WhenCurrencyFormatIsInvalid_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new CreateWalletRequest(Guid.NewGuid(), "TR");

        // Act
        var response = await _client.PostAsJsonAsync("/api/wallets", request);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Contains("CurrencyCode", problem.Errors.Keys);
    }
}
