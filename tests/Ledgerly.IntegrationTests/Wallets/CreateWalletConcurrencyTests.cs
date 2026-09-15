using System.Net;
using System.Net.Http.Json;
using Ledgerly.Api.Contracts.Wallets;
using Ledgerly.Application.Wallets;
using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Persistence;
using Ledgerly.Infrastructure.Wallets;
using Ledgerly.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ledgerly.IntegrationTests.Wallets;

[Collection(PostgresCollection.Name)]
public sealed class CreateWalletConcurrencyTests
{
    private readonly LedgerlyApiFactory _factory;

    public CreateWalletConcurrencyTests(LedgerlyApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Lab", "ConcurrentCreateWallet")]
    public async Task Create_WhenSameWalletIsRequestedConcurrently_ShouldReturnCreatedAndConflict()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var request = new CreateWalletRequest(ownerId, "TRY");
        var gate = new ConcurrentRequestGate(participantCount: 2);

        using var raceFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IWalletRepository>();
                services.AddScoped<IWalletRepository>(serviceProvider =>
                {
                    var dbContext = serviceProvider.GetRequiredService<LedgerlyDbContext>();
                    var repository = new WalletRepository(dbContext);

                    return new CoordinatedWalletRepository(repository, gate);
                });
            })
        );

        using var client = raceFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            }
        );

        HttpResponseMessage[] responses = [];

        try
        {
            // Act
            var firstRequest = client.PostAsJsonAsync("/api/wallets", request);
            var secondRequest = client.PostAsJsonAsync("/api/wallets", request);

            responses = await Task.WhenAll(firstRequest, secondRequest);

            // Assert
            Assert.Single(
                responses,
                response => response.StatusCode == HttpStatusCode.Created
            );

            var failedResponse = Assert.Single(
                responses,
                response => response.StatusCode == HttpStatusCode.Conflict
            );

            var problem = await failedResponse.Content.ReadFromJsonAsync<ProblemDetails>();

            Assert.NotNull(problem);
            Assert.Equal("Wallet already exists", problem.Title);
            Assert.Equal(409, problem.Status);
            Assert.Equal($"Owner '{ownerId}' already has a 'TRY' wallet.", problem.Detail);
            Assert.Equal(1, await _factory.CountWalletsAsync(ownerId));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }

            await _factory.DeleteWalletsAsync(ownerId);
        }
    }

    private sealed class CoordinatedWalletRepository : IWalletRepository
    {
        private readonly IWalletRepository _inner;
        private readonly ConcurrentRequestGate _gate;

        public CoordinatedWalletRepository(
            IWalletRepository inner,
            ConcurrentRequestGate gate
        )
        {
            _inner = inner;
            _gate = gate;
        }

        public Task<Wallet?> GetByIdAsync(Guid walletId, CancellationToken cancellationToken = default)
        {
            return _inner.GetByIdAsync(walletId, cancellationToken);
        }

        public async Task<bool> ExistsAsync(
            Guid ownerId,
            Currency currency,
            CancellationToken cancellationToken = default
        )
        {
            var exists = await _inner.ExistsAsync(ownerId, currency, cancellationToken);

            await _gate.SignalAndWaitAsync(cancellationToken);

            return exists;
        }

        public void Add(Wallet wallet)
        {
            _inner.Add(wallet);
        }
    }

    private sealed class ConcurrentRequestGate
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private readonly int _participantCount;
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _arrivedCount;

        public ConcurrentRequestGate(int participantCount)
        {
            _participantCount = participantCount;
        }

        public async Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrivedCount) == _participantCount)
            {
                _release.TrySetResult();
            }

            await _release.Task.WaitAsync(Timeout, cancellationToken);
        }
    }
}
