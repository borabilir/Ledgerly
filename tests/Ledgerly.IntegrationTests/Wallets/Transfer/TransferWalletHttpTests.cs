using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Ledgerly.Api.Contracts.Transfers;
using Ledgerly.Api.Contracts.Wallets;
using Ledgerly.Application.Abstractions.Messaging;
using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Application.Wallets.TransferWallet;
using Ledgerly.Domain.Ledger;
using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Persistence;
using Ledgerly.Infrastructure.Messaging;
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
    [Trait("Lab", "TransferIdempotency")]
    public async Task Transfer_WhenTheSameRequestIsRetried_ShouldReturnStoredReceiptWithoutMovingMoneyAgain()
    {
        var source = await SeedWallet();
        var destination = await SeedWallet();
        using var client = Client(factory);
        try
        {
            await Fund(client, source.Id, 250m);
            const string idempotencyKey = "transfer-retry-1";

            using var firstResponse = await PostTransfer(
                client, source.Id, destination.Id, 100m, idempotencyKey);
            using var retryResponse = await PostTransfer(
                client, source.Id, destination.Id, 100m, idempotencyKey);

            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
            var firstReceipt = (await firstResponse.Content.ReadFromJsonAsync<CreateTransferResponse>())!;
            var retryReceipt = (await retryResponse.Content.ReadFromJsonAsync<CreateTransferResponse>())!;
            Assert.Equal(firstReceipt, retryReceipt);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
            Assert.Equal(150m, await db.Wallets.Where(wallet => wallet.Id == source.Id)
                .Select(wallet => wallet.Balance).SingleAsync());
            Assert.Equal(100m, await db.Wallets.Where(wallet => wallet.Id == destination.Id)
                .Select(wallet => wallet.Balance).SingleAsync());
            Assert.Equal(1, await db.JournalEntries.CountAsync(entry =>
                entry.Id == firstReceipt.JournalEntryId));
            Assert.Equal(1, await db.WalletTransfers.CountAsync(transfer =>
                transfer.Id == firstReceipt.TransferId));
        }
        finally { await Cleanup(source.Id, destination.Id); }
    }

    [Fact]
    [Trait("Lab", "TransferIdempotency")]
    public async Task Transfer_WhenIdempotencyKeyIsMissing_ShouldReturn400WithoutWriting()
    {
        var source = await SeedWallet();
        var destination = await SeedWallet();
        using var client = Client(factory);
        try
        {
            using var response = await client.PostAsJsonAsync("/api/transfers",
                new CreateTransferRequest(source.Id, destination.Id, 10m));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Invalid request", (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
            Assert.False(await db.WalletTransfers.AnyAsync(transfer =>
                transfer.SourceWalletId == source.Id));
        }
        finally { await Cleanup(source.Id, destination.Id); }
    }

    [Fact]
    [Trait("Lab", "TransferIdempotency")]
    public async Task Transfer_WhenTheSameKeyIsReusedForDifferentPayload_ShouldReturn409WithoutSecondTransfer()
    {
        var source = await SeedWallet();
        var destination = await SeedWallet();
        using var client = Client(factory);
        try
        {
            await Fund(client, source.Id, 100m);
            const string idempotencyKey = "transfer-payload-conflict";
            using var firstResponse = await PostTransfer(
                client, source.Id, destination.Id, 30m, idempotencyKey);
            using var conflictingResponse = await PostTransfer(
                client, source.Id, destination.Id, 31m, idempotencyKey);

            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, conflictingResponse.StatusCode);
            Assert.Equal("Transfer idempotency key conflict",
                (await conflictingResponse.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
            Assert.Equal(70m, await db.Wallets.Where(wallet => wallet.Id == source.Id)
                .Select(wallet => wallet.Balance).SingleAsync());
            Assert.Equal(30m, await db.Wallets.Where(wallet => wallet.Id == destination.Id)
                .Select(wallet => wallet.Balance).SingleAsync());
        }
        finally { await Cleanup(source.Id, destination.Id); }
    }

    [Fact]
    [Trait("Lab", "TransferIdempotency")]
    public async Task Transfer_WhenTheSamePayloadUsesDifferentKeys_ShouldCreateTwoTransfers()
    {
        var source = await SeedWallet();
        var destination = await SeedWallet();
        using var client = Client(factory);
        try
        {
            await Fund(client, source.Id, 100m);
            using var firstResponse = await PostTransfer(
                client, source.Id, destination.Id, 40m, "first-transfer-intent");
            using var secondResponse = await PostTransfer(
                client, source.Id, destination.Id, 40m, "second-transfer-intent");

            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
            var firstReceipt = (await firstResponse.Content.ReadFromJsonAsync<CreateTransferResponse>())!;
            var secondReceipt = (await secondResponse.Content.ReadFromJsonAsync<CreateTransferResponse>())!;
            Assert.NotEqual(firstReceipt.TransferId, secondReceipt.TransferId);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
            Assert.Equal(20m, await db.Wallets.Where(wallet => wallet.Id == source.Id)
                .Select(wallet => wallet.Balance).SingleAsync());
            Assert.Equal(80m, await db.Wallets.Where(wallet => wallet.Id == destination.Id)
                .Select(wallet => wallet.Balance).SingleAsync());
            Assert.Equal(2, await db.WalletTransfers.CountAsync(transfer =>
                transfer.SourceWalletId == source.Id));
        }
        finally { await Cleanup(source.Id, destination.Id); }
    }

    [Fact]
    [Trait("Lab", "TransferIdempotency")]
    public async Task Transfer_WhenTheSameKeyArrivesConcurrently_ShouldCommitOnceAndReplayOneReceipt()
    {
        var source = await SeedWallet();
        var destination = await SeedWallet();
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
            const string idempotencyKey = "parallel-transfer-retry";
            var responses = await Task.WhenAll(
                PostTransfer(client, source.Id, destination.Id, 40m, idempotencyKey),
                PostTransfer(client, source.Id, destination.Id, 40m, idempotencyKey));
            try
            {
                Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
                var receipts = await Task.WhenAll(responses.Select(async response =>
                    (await response.Content.ReadFromJsonAsync<CreateTransferResponse>())!));
                Assert.Equal(receipts[0], receipts[1]);

                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
                Assert.Equal(60m, await db.Wallets.Where(wallet => wallet.Id == source.Id)
                    .Select(wallet => wallet.Balance).SingleAsync());
                Assert.Equal(40m, await db.Wallets.Where(wallet => wallet.Id == destination.Id)
                    .Select(wallet => wallet.Balance).SingleAsync());
                Assert.Equal(1, await db.WalletTransfers.CountAsync(transfer =>
                    transfer.SourceWalletId == source.Id && transfer.IdempotencyKey == idempotencyKey));
                Assert.Equal(1, await db.JournalEntries.CountAsync(entry => entry.Id == receipts[0].JournalEntryId));
            }
            finally { foreach (var response in responses) response.Dispose(); }
        }
        finally { await Cleanup(source.Id, destination.Id); }
    }

    [Fact]
    [Trait("Lab", "TransactionalOutbox")]
    public async Task Transfer_WhenPublisherFails_ShouldCommitOutboxAndPublishOnRetry()
    {
        var source = await SeedWallet();
        var destination = await SeedWallet();
        var publisher = new SwitchableEventPublisher { ShouldFail = true };
        using var failingApp = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IIntegrationEventPublisher>();
            services.AddSingleton<IIntegrationEventPublisher>(publisher);
        }));
        using var client = Client(failingApp);
        try
        {
            await Fund(client, source.Id, 100m);
            const string idempotencyKey = "outbox-failure-reproduce";

            using var response = await PostTransfer(
                client, source.Id, destination.Id, 40m, idempotencyKey);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var receipt = (await response.Content.ReadFromJsonAsync<CreateTransferResponse>())!;
            Assert.Empty(publisher.Attempts);
            Assert.Empty(publisher.Delivered);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
                Assert.Equal(60m, await db.Wallets.Where(wallet => wallet.Id == source.Id)
                    .Select(wallet => wallet.Balance).SingleAsync());
                Assert.Equal(40m, await db.Wallets.Where(wallet => wallet.Id == destination.Id)
                    .Select(wallet => wallet.Balance).SingleAsync());
                var committedTransfer = await db.WalletTransfers.AsNoTracking().SingleAsync(transfer =>
                    transfer.SourceWalletId == source.Id && transfer.IdempotencyKey == idempotencyKey);
                Assert.Equal(receipt.TransferId, committedTransfer.Id);
                var pendingMessage = await db.OutboxMessages.AsNoTracking().SingleAsync(message =>
                    message.AggregateId == receipt.TransferId);
                Assert.Equal(TransferCompletedIntegrationEvent.EventType, pendingMessage.Type);
                Assert.Null(pendingMessage.ProcessedAtUtc);
                Assert.Equal(0, pendingMessage.AttemptCount);
            }

            // Retrying the HTTP request replays the receipt and does not create a second event.
            using var retryResponse = await PostTransfer(
                client, source.Id, destination.Id, 40m, idempotencyKey);
            Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
                Assert.Equal(1, await db.OutboxMessages.CountAsync(message =>
                    message.AggregateId == receipt.TransferId));
            }

            await ProcessOutbox(failingApp);
            Assert.Single(publisher.Attempts);
            Assert.Empty(publisher.Delivered);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
                var failedMessage = await db.OutboxMessages.AsNoTracking().SingleAsync(message =>
                    message.AggregateId == receipt.TransferId);
                Assert.Null(failedMessage.ProcessedAtUtc);
                Assert.Equal(1, failedMessage.AttemptCount);
                Assert.Equal("The simulated broker is unavailable.", failedMessage.LastError);
            }

            publisher.ShouldFail = false;
            await ProcessOutbox(failingApp);
            Assert.Equal(2, publisher.Attempts.Count);
            var delivered = Assert.Single(publisher.Delivered);
            Assert.Equal(receipt.TransferId, delivered.AggregateId);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
                var processedMessage = await db.OutboxMessages.AsNoTracking().SingleAsync(message =>
                    message.AggregateId == receipt.TransferId);
                Assert.NotNull(processedMessage.ProcessedAtUtc);
                Assert.Equal(2, processedMessage.AttemptCount);
                Assert.Null(processedMessage.LastError);
            }
        }
        finally { await Cleanup(source.Id, destination.Id); }
    }

    private static async Task ProcessOutbox(WebApplicationFactory<Program> app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync();
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

    private static async Task<HttpResponseMessage> PostTransfer(
        HttpClient client,
        Guid source,
        Guid destination,
        decimal amount,
        string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/transfers")
        {
            Content = JsonContent.Create(new CreateTransferRequest(source, destination, amount)),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

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
        var transferIds = await db.WalletTransfers.Where(transfer => walletIds.Contains(transfer.SourceWalletId)
            || walletIds.Contains(transfer.DestinationWalletId)).Select(transfer => transfer.Id).ToArrayAsync();
        await db.OutboxMessages.Where(message => transferIds.Contains(message.AggregateId)).ExecuteDeleteAsync();
        await db.WalletTransfers.Where(transfer => walletIds.Contains(transfer.SourceWalletId)
            || walletIds.Contains(transfer.DestinationWalletId)).ExecuteDeleteAsync();
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

    private sealed class SwitchableEventPublisher : IIntegrationEventPublisher
    {
        public bool ShouldFail { get; set; }
        public List<IntegrationEventMessage> Attempts { get; } = [];
        public List<IntegrationEventMessage> Delivered { get; } = [];

        public Task PublishAsync(
            IntegrationEventMessage message,
            CancellationToken cancellationToken = default)
        {
            Attempts.Add(message);
            if (ShouldFail)
            {
                throw new InvalidOperationException("The simulated broker is unavailable.");
            }

            Delivered.Add(message);
            return Task.CompletedTask;
        }
    }
}
