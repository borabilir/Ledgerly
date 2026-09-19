using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Ledgerly.Api.Contracts.Transfers;
using Ledgerly.Api.Contracts.Wallets;
using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Domain.Ledger;
using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Persistence;
using Ledgerly.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ledgerly.IntegrationTests.Wallets.Transfer;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Lab", "WalletTransfer")]
public sealed class TransferWalletHttpTests(LedgerlyApiFactory factory)
{
    private static HttpClient Client(WebApplicationFactory<Program> app) => app.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    [Fact]
    public async Task Transfer_ShouldMoveBalanceAndPersistOneBalancedJournal()
    {
        var source = await SeedWallet();
        var destination = await SeedWallet();
        using var client = Client(factory);
        try
        {
            await Fund(client, source.Id, 100m);

            using var response = await PostTransfer(client, source.Id, destination.Id, 40m);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var receipt = (await response.Content.ReadFromJsonAsync<CreateTransferResponse>())!;
            Assert.Equal(60m, receipt.SourceBalance);
            Assert.Equal(40m, receipt.DestinationBalance);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
            var wallets = await db.Wallets.Where(wallet =>
                    wallet.Id == source.Id || wallet.Id == destination.Id)
                .ToDictionaryAsync(wallet => wallet.Id);
            Assert.Equal(60m, wallets[source.Id].Balance);
            Assert.Equal(40m, wallets[destination.Id].Balance);
            var sourceAccount = await db.LedgerAccounts.SingleAsync(account => account.WalletId == source.Id);
            var destinationAccount = await db.LedgerAccounts.SingleAsync(account => account.WalletId == destination.Id);
            var journal = await db.JournalEntries.Include(entry => entry.Postings)
                .SingleAsync(entry => entry.Id == receipt.JournalEntryId);
            Assert.Collection(journal.Postings.OrderBy(posting => posting.Sequence),
                posting =>
                {
                    Assert.Equal(sourceAccount.Id, posting.AccountId);
                    Assert.Equal(PostingDirection.Debit, posting.Direction);
                    Assert.Equal(40m, posting.Amount);
                },
                posting =>
                {
                    Assert.Equal(destinationAccount.Id, posting.AccountId);
                    Assert.Equal(PostingDirection.Credit, posting.Direction);
                    Assert.Equal(40m, posting.Amount);
                });
        }
        finally { await Cleanup(source.Id, destination.Id); }
    }

    [Fact]
    public async Task Transfer_WhenFundsAreInsufficient_ShouldReturn409WithoutAnyTransferWrites()
    {
        var source = await SeedWallet();
        var destination = await SeedWallet();
        using var client = Client(factory);
        try
        {
            await Fund(client, source.Id, 10m);
            await using var beforeScope = factory.Services.CreateAsyncScope();
            var beforeDb = beforeScope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
            var journalsBefore = await beforeDb.JournalEntries.CountAsync();

            using var response = await PostTransfer(client, source.Id, destination.Id, 11m);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("Insufficient funds", (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
            Assert.Equal(10m, await db.Wallets.Where(w => w.Id == source.Id).Select(w => w.Balance).SingleAsync());
            Assert.Equal(0m, await db.Wallets.Where(w => w.Id == destination.Id).Select(w => w.Balance).SingleAsync());
            Assert.Equal(journalsBefore, await db.JournalEntries.CountAsync());
            Assert.False(await db.LedgerAccounts.AnyAsync(account => account.WalletId == destination.Id));
        }
        finally { await Cleanup(source.Id, destination.Id); }
    }

    [Fact]
    public async Task Transfer_WhenWalletsAreSame_ShouldReturn400WithoutChangingBalance()
    {
        var wallet = await SeedWallet();
        using var client = Client(factory);
        try
        {
            await Fund(client, wallet.Id, 100m);
            using var response = await PostTransfer(client, wallet.Id, wallet.Id, 10m);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Invalid transfer", (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
            Assert.Equal(100m, await db.Wallets.Where(w => w.Id == wallet.Id).Select(w => w.Balance).SingleAsync());
        }
        finally { await Cleanup(wallet.Id); }
    }

    [Fact]
    public async Task Transfer_WhenDestinationIsMissing_ShouldReturn404WithoutChangingSource()
    {
        var source = await SeedWallet();
        using var client = Client(factory);
        try
        {
            await Fund(client, source.Id, 100m);
            using var response = await PostTransfer(client, source.Id, Guid.NewGuid(), 10m);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
            Assert.Equal(100m, await db.Wallets.Where(w => w.Id == source.Id).Select(w => w.Balance).SingleAsync());
        }
        finally { await Cleanup(source.Id); }
    }

    [Fact]
    public async Task Transfer_WhenSameSourceBalanceIsSpentConcurrently_ShouldCommitOnlyOneTransfer()
    {
        var source = await SeedWallet();
        var firstDestination = await SeedWallet();
        var secondDestination = await SeedWallet();
        using var setupClient = Client(factory);
        await Fund(setupClient, source.Id, 100m);
        var gate = new SaveGate();
        using var racing = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork>(provider => new CoordinatedSave(
                provider.GetRequiredService<LedgerlyDbContext>(), gate));
        }));
        using var client = Client(racing);
        try
        {
            var responses = await Task.WhenAll(
                PostTransfer(client, source.Id, firstDestination.Id, 80m),
                PostTransfer(client, source.Id, secondDestination.Id, 80m));
            try
            {
                Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
                Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
                Assert.Equal(2, gate.JournalIds.Count);
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
                var walletIds = new[] { source.Id, firstDestination.Id, secondDestination.Id };
                var balances = await db.Wallets.Where(wallet => walletIds.Contains(wallet.Id))
                    .ToDictionaryAsync(wallet => wallet.Id, wallet => wallet.Balance);
                Assert.Equal(20m, balances[source.Id]);
                Assert.Equal(80m, balances[firstDestination.Id] + balances[secondDestination.Id]);
                Assert.Equal(1, await db.JournalEntries.CountAsync(entry => gate.JournalIds.Contains(entry.Id)));
                Assert.Equal(2, await db.Postings.CountAsync(posting => gate.JournalIds.Contains(posting.JournalEntryId)));
                Assert.Equal(1, await db.LedgerAccounts.CountAsync(account =>
                    (account.WalletId == firstDestination.Id || account.WalletId == secondDestination.Id)));
            }
            finally { foreach (var response in responses) response.Dispose(); }
        }
        finally { await Cleanup(source.Id, firstDestination.Id, secondDestination.Id); }
    }

    private static Task<HttpResponseMessage> PostTransfer(
        HttpClient client, Guid source, Guid destination, decimal amount) =>
        client.PostAsJsonAsync("/api/transfers", new CreateTransferRequest(source, destination, amount));

    private static async Task Fund(HttpClient client, Guid walletId, decimal amount)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/wallets/{walletId}/test-deposits")
        {
            Content = JsonContent.Create(new TestDepositRequest(amount)),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<Wallet> SeedWallet()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
        var wallet = Wallet.Create(Guid.NewGuid(), Currency.FromCode("TRY"), DateTimeOffset.UtcNow);
        db.Wallets.Add(wallet);
        await db.SaveChangesAsync();
        return wallet;
    }

    private async Task Cleanup(params Guid[] walletIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
        var accountIds = db.LedgerAccounts.Where(account =>
            account.WalletId != null && walletIds.Contains(account.WalletId.Value)).Select(account => account.Id);
        var journalIds = await db.Postings.Where(posting => accountIds.Contains(posting.AccountId))
            .Select(posting => posting.JournalEntryId).Distinct().ToArrayAsync();
        await db.TestDepositOperations.Where(operation => walletIds.Contains(operation.WalletId)).ExecuteDeleteAsync();
        await db.Postings.Where(posting => journalIds.Contains(posting.JournalEntryId)).ExecuteDeleteAsync();
        await db.JournalEntries.Where(entry => journalIds.Contains(entry.Id)).ExecuteDeleteAsync();
        await db.LedgerAccounts.Where(account =>
            account.WalletId != null && walletIds.Contains(account.WalletId.Value)).ExecuteDeleteAsync();
        // Test deposits share one funding account per currency. Remove it only after
        // none of the remaining test data references it.
        await db.LedgerAccounts.Where(account => account.WalletId == null
            && !db.Postings.Any(posting => posting.AccountId == account.Id)).ExecuteDeleteAsync();
        await db.Wallets.Where(wallet => walletIds.Contains(wallet.Id)).ExecuteDeleteAsync();
    }

    private sealed class SaveGate
    {
        public ConcurrentBag<Guid> JournalIds { get; } = [];
        private int _count;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task Wait(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _count) == 2) _release.TrySetResult();
            await _release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
    }

    private sealed class CoordinatedSave(LedgerlyDbContext db, SaveGate gate) : IUnitOfWork
    {
        public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            gate.JournalIds.Add(db.JournalEntries.Local.Single().Id);
            await gate.Wait(cancellationToken);
            await ((IUnitOfWork)db).SaveChangesAsync(cancellationToken);
        }
    }
}
