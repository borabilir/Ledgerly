using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Persistence;
using Ledgerly.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ledgerly.IntegrationTests.Wallets;

[Collection(PostgresCollection.Name)]
public sealed class GetWalletHttpTests
{
    private readonly LedgerlyApiFactory _factory;

    public GetWalletHttpTests(LedgerlyApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetById_WhenWalletExists_ShouldReturnPersistedDetails()
    {
        var ownerId = Guid.NewGuid();
        var wallet = Wallet.Create(ownerId, Currency.FromCode("TRY"), PostgresFixture.FixedUtcNow);
        using var client = CreateClient();

        try
        {
            await using (var scope = _factory.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
                dbContext.Wallets.Add(wallet);
                await dbContext.SaveChangesAsync();
            }

            using var response = await client.GetAsync($"/api/wallets/{wallet.Id}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var body = document.RootElement;
            Assert.Equal(wallet.Id, body.GetProperty("walletId").GetGuid());
            Assert.Equal(ownerId, body.GetProperty("ownerId").GetGuid());
            Assert.Equal("TRY", body.GetProperty("currencyCode").GetString());
            Assert.Equal("Active", body.GetProperty("status").GetString());
            Assert.Equal(0m, body.GetProperty("balance").GetDecimal());
            Assert.Equal(wallet.CreatedAtUtc, body.GetProperty("createdAtUtc").GetDateTimeOffset());
            Assert.Equal(1, await _factory.CountWalletsAsync(ownerId));

            // A different ID must not return an arbitrary wallet from the table.
            var missingId = Guid.NewGuid();
            using var missingResponse = await client.GetAsync($"/api/wallets/{missingId}");
            Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
            var problem = await missingResponse.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Equal(404, problem.Status);
            Assert.Equal("Wallet not found", problem.Title);
            Assert.Equal($"Wallet '{missingId}' was not found.", problem.Detail);
        }
        finally
        {
            await _factory.DeleteWalletsAsync(ownerId);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetById_WhenWalletDoesNotExist_ShouldReturnNotFoundProblem()
    {
        var walletId = Guid.NewGuid();
        using var client = CreateClient();

        using var response = await client.GetAsync($"/api/wallets/{walletId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
        Assert.Equal("Wallet not found", problem.Title);
        Assert.Equal($"Wallet '{walletId}' was not found.", problem.Detail);
        Assert.False(await _factory.WalletExistsAsync(walletId));
    }

    private HttpClient CreateClient() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }
    );
}
