using System.Net;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using Ledgerly.Api.Contracts.Wallets;
using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Application.Ledger;
using Ledgerly.Domain.Ledger;
using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Ledger;
using Ledgerly.Infrastructure.Persistence;
using Ledgerly.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ledgerly.IntegrationTests.Wallets.TestDeposit;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Lab", "TestDeposit")]
public sealed class TestDepositHttpTests(LedgerlyApiFactory factory)
{
    private static HttpClient Client(WebApplicationFactory<Program> app) => app.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
    private static string Url(Guid id) => $"/api/wallets/{id}/test-deposits";
    private static async Task<HttpResponseMessage> PostDepositAsync(HttpClient client, Guid walletId, decimal amount,
        string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url(walletId))
        {
            Content = JsonContent.Create(new TestDepositRequest(amount)),
        };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Deposit_ShouldProvisionAccountsAndAccumulateWithBalancedJournals()
    {
        var wallet = await SeedWallet();
        using var client = Client(factory);
        try
        {
            const string firstKey = "first-deposit";
            using var first = await PostDepositAsync(client, wallet.Id, 100m, firstKey);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var receipt = (await first.Content.ReadFromJsonAsync<TestDepositResponse>())!;
            Assert.Equal(wallet.Id, receipt.WalletId);
            Assert.Equal("TRY", receipt.CurrencyCode);
            Assert.Equal(100m, receipt.Amount);
            Assert.Equal(100m, receipt.Balance);
            using var second = await PostDepositAsync(client, wallet.Id, 0.1234m);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            var next = (await second.Content.ReadFromJsonAsync<TestDepositResponse>())!;
            Assert.NotEqual(receipt.JournalEntryId, next.JournalEntryId);
            Assert.Equal(100.1234m, next.Balance);
            using var oldRetry = await PostDepositAsync(client, wallet.Id, 100m, firstKey);
            Assert.Equal(HttpStatusCode.OK, oldRetry.StatusCode);
            Assert.Equal(receipt, await oldRetry.Content.ReadFromJsonAsync<TestDepositResponse>());
            var get = await client.GetFromJsonAsync<GetWalletResponse>($"/api/wallets/{wallet.Id}");
            Assert.Equal(next.Balance, get!.Balance);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
            var customer = await db.LedgerAccounts.SingleAsync(a => a.WalletId == wallet.Id);
            var journal = await db.JournalEntries.Include(j => j.Postings).SingleAsync(j => j.Id == receipt.JournalEntryId);
            var credit = Assert.Single(journal.Postings, p => p.Direction == PostingDirection.Credit);
            var debit = Assert.Single(journal.Postings, p => p.Direction == PostingDirection.Debit);
            Assert.Equal(customer.Id, credit.AccountId);
            Assert.Equal(100m, credit.Amount);
            Assert.Equal(credit.Amount, debit.Amount);
            var funding = await db.LedgerAccounts.SingleAsync(a => a.Id == debit.AccountId);
            Assert.Null(funding.WalletId);
            Assert.Equal(LedgerAccountType.Asset, funding.Type);
            Assert.Equal(2, await db.Postings.CountAsync(p => p.AccountId == customer.Id));
            Assert.Equal(100.1234m, await db.Postings.Where(p => p.AccountId == customer.Id).SumAsync(p => p.Amount));
        }
        finally { await Cleanup(wallet.Id); }
    }

    [Fact]
    [Trait("Lab", "TestDepositIdempotency")]
    public async Task Deposit_WhenIdenticalRequestIsRetried_ShouldReplayWithoutCreditingTwice()
    {
        var wallet = await SeedWallet();
        using var client = Client(factory);
        try
        {
            const string key = "same-logical-deposit";
            using var first = await PostDepositAsync(client, wallet.Id, 100m, key);
            using var retry = await PostDepositAsync(client, wallet.Id, 100m, key);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            var firstReceipt = (await first.Content.ReadFromJsonAsync<TestDepositResponse>())!;
            var retryReceipt = (await retry.Content.ReadFromJsonAsync<TestDepositResponse>())!;
            Assert.Equal(firstReceipt, retryReceipt);
            Assert.Equal(100m, retryReceipt.Balance);

            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
            Assert.Equal(1, await db.JournalEntries.CountAsync(j => j.Id == firstReceipt.JournalEntryId));
            Assert.Equal(2, await db.Postings.CountAsync(p => p.JournalEntryId == firstReceipt.JournalEntryId));
            Assert.Equal(1, await db.TestDepositOperations.CountAsync(o => o.WalletId == wallet.Id));
            Assert.Equal(100m, await db.Wallets.Where(w => w.Id == wallet.Id).Select(w => w.Balance).SingleAsync());
        }
        finally { await Cleanup(wallet.Id); }
    }

    [Fact]
    [Trait("Lab", "TestDepositIdempotency")]
    public async Task Deposit_WhenKeyIsMissing_ShouldReturn400WithoutWrites()
    {
        var wallet = await SeedWallet();
        using var client = Client(factory);
        try
        {
            using var response = await client.PostAsJsonAsync(Url(wallet.Id), new TestDepositRequest(100m));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Invalid idempotency key", (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
            await AssertUnchanged(wallet.Id);
        }
        finally { await Cleanup(wallet.Id); }
    }

    [Fact]
    [Trait("Lab", "TestDepositIdempotency")]
    public async Task Deposit_WhenKeyIsTooLong_ShouldReturn400WithoutWrites()
    {
        var wallet = await SeedWallet();
        using var client = Client(factory);
        try
        {
            using var response = await PostDepositAsync(client, wallet.Id, 100m, new string('x', 129));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            await AssertUnchanged(wallet.Id);
        }
        finally { await Cleanup(wallet.Id); }
    }

    [Fact]
    [Trait("Lab", "TestDepositIdempotency")]
    public async Task Deposit_WhenDifferentWalletsUseSameKey_ShouldCreateSeparateOperations()
    {
        var firstWallet = await SeedWallet();
        var secondWallet = await SeedWallet();
        using var client = Client(factory);
        try
        {
            const string key = "shared-by-two-wallets";
            using var first = await PostDepositAsync(client, firstWallet.Id, 100m, key);
            using var second = await PostDepositAsync(client, secondWallet.Id, 100m, key);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            var firstReceipt = (await first.Content.ReadFromJsonAsync<TestDepositResponse>())!;
            var secondReceipt = (await second.Content.ReadFromJsonAsync<TestDepositResponse>())!;
            Assert.NotEqual(firstReceipt.JournalEntryId, secondReceipt.JournalEntryId);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
            Assert.Equal(2, await db.TestDepositOperations.CountAsync(o => o.Key == key));
        }
        finally { await Cleanup(firstWallet.Id, secondWallet.Id); }
    }

    [Fact]
    [Trait("Lab", "TestDepositIdempotency")]
    public async Task Deposit_WhenKeyIsReusedWithDifferentAmount_ShouldReturn409WithoutAnotherJournal()
    {
        var wallet = await SeedWallet();
        using var client = Client(factory);
        try
        {
            const string key = "same-key-different-amount";
            using var first = await PostDepositAsync(client, wallet.Id, 100m, key);
            using var second = await PostDepositAsync(client, wallet.Id, 200m, key);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal("Idempotency key conflict", (await second.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
            var receipt = (await first.Content.ReadFromJsonAsync<TestDepositResponse>())!;
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
            Assert.Equal(1, await db.TestDepositOperations.CountAsync(o => o.WalletId == wallet.Id));
            Assert.Equal(1, await db.JournalEntries.CountAsync(j => j.Id == receipt.JournalEntryId));
            Assert.Equal(100m, await db.Wallets.Where(w => w.Id == wallet.Id).Select(w => w.Balance).SingleAsync());
        }
        finally { await Cleanup(wallet.Id); }
    }

    [Fact]
    [Trait("Lab", "TestDepositIdempotency")]
    public async Task Deposit_WhenSameKeyRaces_ShouldCommitOnceAndReplayOneReceipt()
    {
        var wallet = await SeedWallet();
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
            const string key = "parallel-same-key";
            var responses = await Task.WhenAll(
                PostDepositAsync(client, wallet.Id, 100m, key),
                PostDepositAsync(client, wallet.Id, 100m, key));
            try
            {
                Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.OK);
                Assert.All(responses, response => Assert.True(
                    response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict));
                using var settledRetry = await PostDepositAsync(client, wallet.Id, 100m, key);
                Assert.Equal(HttpStatusCode.OK, settledRetry.StatusCode);
                var receipt = (await settledRetry.Content.ReadFromJsonAsync<TestDepositResponse>())!;
                foreach (var response in responses.Where(response => response.StatusCode == HttpStatusCode.OK))
                    Assert.Equal(receipt, await response.Content.ReadFromJsonAsync<TestDepositResponse>());
                Assert.Equal(2, gate.JournalIds.Count);
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
                Assert.Equal(1, await db.TestDepositOperations.CountAsync(o => o.WalletId == wallet.Id));
                Assert.Equal(1, await db.JournalEntries.CountAsync(j => gate.JournalIds.Contains(j.Id)));
                Assert.Equal(2, await db.Postings.CountAsync(p => gate.JournalIds.Contains(p.JournalEntryId)));
                Assert.Equal(100m, await db.Wallets.Where(w => w.Id == wallet.Id).Select(w => w.Balance).SingleAsync());
            }
            finally { foreach (var response in responses) response.Dispose(); }
        }
        finally { await Cleanup(wallet.Id); }
    }

    [Fact]
    [Trait("Lab", "TestDepositIdempotency")]
    public async Task Deposit_WhenSameKeyRacesWithDifferentAmounts_ShouldReturnOneSuccessAndOneConflict()
    {
        var wallet = await SeedWallet();
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
            const string key = "parallel-different-amount";
            var responses = await Task.WhenAll(
                PostDepositAsync(client, wallet.Id, 100m, key),
                PostDepositAsync(client, wallet.Id, 200m, key));
            try
            {
                var success = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
                var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
                Assert.Equal("Idempotency key conflict",
                    (await conflict.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
                var receipt = (await success.Content.ReadFromJsonAsync<TestDepositResponse>())!;
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
                Assert.Equal(1, await db.TestDepositOperations.CountAsync(o => o.WalletId == wallet.Id));
                Assert.Equal(1, await db.JournalEntries.CountAsync(j => j.Id == receipt.JournalEntryId));
                Assert.Equal(receipt.Amount, await db.Wallets.Where(w => w.Id == wallet.Id).Select(w => w.Balance).SingleAsync());
            }
            finally { foreach (var response in responses) response.Dispose(); }
        }
        finally { await Cleanup(wallet.Id); }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.00001")]
    [InlineData("1000000000000000")]
    public async Task Deposit_WhenAmountInvalid_ShouldReturn400WithoutWrites(string input)
    {
        var wallet = await SeedWallet();
        using var client = Client(factory);
        try
        {
            var amount = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
            using var response = await PostDepositAsync(client, wallet.Id, amount);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(400, (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Status);
            await AssertUnchanged(wallet.Id);
        }
        finally { await Cleanup(wallet.Id); }
    }

    [Fact]
    public async Task Deposit_WhenWalletMissing_ShouldReturn404()
    {
        using var client = Client(factory);
        using var response = await PostDepositAsync(client, Guid.NewGuid(), 10m);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Wallet not found", (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
    }

    [Fact]
    public async Task Deposit_InProduction_ShouldReturn404WithoutChangingExistingWallet()
    {
        var wallet = await SeedWallet();
        using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = Client(production);
        try
        {
            using var response = await PostDepositAsync(client, wallet.Id, 10m);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("Endpoint not available", (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
            await AssertUnchanged(wallet.Id);
        }
        finally { await Cleanup(wallet.Id); }
    }

    [Theory]
    [InlineData("balance")]
    [InlineData("wallet-account")]
    [InlineData("funding-account")]
    public async Task Deposit_WhenRequestsRace_ShouldCommitOneCompleteOperation(string scenario)
    {
        var firstWallet = await SeedWallet();
        var secondWallet = scenario == "funding-account" ? await SeedWallet() : firstWallet;
        Guid? seededFunding = null;
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
            if (scenario != "funding-account")
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
                var accounts = new LedgerAccountRepository(db);
                var funding = LedgerAccount.CreateTestFunding(firstWallet.Currency, DateTimeOffset.UtcNow);
                seededFunding = funding.Id;
                accounts.Add(funding);
                if (scenario == "balance")
                    accounts.Add(LedgerAccount.CreateForWallet(firstWallet.Id, firstWallet.Currency, DateTimeOffset.UtcNow));
                await ((IUnitOfWork)db).SaveChangesAsync();
            }
            var responses = await Task.WhenAll(
                PostDepositAsync(client, firstWallet.Id, 10m),
                PostDepositAsync(client, secondWallet.Id, 20m));
            try
            {
                var success = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
                var conflict = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
                Assert.Equal("Ledger write conflict", (await conflict.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
                var receipt = (await success.Content.ReadFromJsonAsync<TestDepositResponse>())!;
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
                var ids = new[] { firstWallet.Id, secondWallet.Id };
                var attemptedJournals = gate.JournalIds.ToArray();
                Assert.Equal(2, attemptedJournals.Length);
                Assert.Equal(1, await db.JournalEntries.CountAsync(j => attemptedJournals.Contains(j.Id)));
                Assert.Equal(2, await db.Postings.CountAsync(p => attemptedJournals.Contains(p.JournalEntryId)));
                Assert.Equal(receipt.Amount, await db.Wallets.Where(w => ids.Contains(w.Id)).SumAsync(w => w.Balance));
                var customer = await db.LedgerAccounts.SingleAsync(a => a.WalletId == receipt.WalletId);
                Assert.Equal(1, await db.Postings.CountAsync(p => p.AccountId == customer.Id));
                var postings = await db.Postings.Where(p => p.JournalEntryId == receipt.JournalEntryId).ToListAsync();
                Assert.Equal(2, postings.Count);
                Assert.All(postings, p => Assert.Equal(receipt.Amount, p.Amount));
                Assert.Equal(1, await db.LedgerAccounts.CountAsync(a => a.WalletId == null));
                if (scenario == "funding-account")
                {
                    var loserId = receipt.WalletId == firstWallet.Id ? secondWallet.Id : firstWallet.Id;
                    Assert.False(await db.LedgerAccounts.AnyAsync(a => a.WalletId == loserId));
                }
            }
            finally { foreach (var response in responses) response.Dispose(); }
        }
        finally
        {
            await Cleanup(firstWallet.Id, secondWallet.Id);
            if (seededFunding is { } fundingId)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>().LedgerAccounts
                    .Where(a => a.Id == fundingId).ExecuteDeleteAsync();
            }
        }
    }

    [Fact]
    public async Task Deposit_WhenPostingViolatesForeignKey_ShouldRollBackAccountsJournalAndBalance()
    {
        var wallet = await SeedWallet();
        var observed = new FailedJournal();
        using var broken = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IJournalEntryRepository>();
            services.AddScoped<IJournalEntryRepository>(provider => new InvalidPostingRepository(
                provider.GetRequiredService<LedgerlyDbContext>(), observed));
        }));
        using var client = Client(broken);
        try
        {
            using var response = await PostDepositAsync(client, wallet.Id, 10m);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
            Assert.Equal("The server could not process the request.", problem.Detail);
            Assert.NotEqual(Guid.Empty, observed.Id);
            await AssertUnchanged(wallet.Id);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
            Assert.False(await db.JournalEntries.AnyAsync(j => j.Id == observed.Id));
            Assert.False(await db.Postings.AnyAsync(p => p.JournalEntryId == observed.Id));
            Assert.False(await db.LedgerAccounts.AnyAsync(a => observed.AccountIds.Contains(a.Id)));
        }
        finally { await Cleanup(wallet.Id); }
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

    private async Task AssertUnchanged(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
        Assert.Equal(0m, await db.Wallets.Where(w => w.Id == id).Select(w => w.Balance).SingleAsync());
        Assert.False(await db.LedgerAccounts.AnyAsync(a => a.WalletId == id));
        Assert.False(await db.TestDepositOperations.AnyAsync(o => o.WalletId == id));
    }

    private async Task Cleanup(params Guid[] walletIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
        var customers = db.LedgerAccounts.Where(a => a.WalletId != null && walletIds.Contains(a.WalletId.Value)).Select(a => a.Id);
        var journalIds = await db.Postings.Where(p => customers.Contains(p.AccountId)).Select(p => p.JournalEntryId).Distinct().ToArrayAsync();
        var postingAccounts = db.Postings.Where(p => journalIds.Contains(p.JournalEntryId)).Select(p => p.AccountId);
        var fundingIds = await db.LedgerAccounts.Where(a => a.WalletId == null && postingAccounts.Contains(a.Id)).Select(a => a.Id).ToArrayAsync();
        await db.TestDepositOperations.Where(operation => walletIds.Contains(operation.WalletId)).ExecuteDeleteAsync();
        await db.Postings.Where(p => journalIds.Contains(p.JournalEntryId)).ExecuteDeleteAsync();
        await db.JournalEntries.Where(j => journalIds.Contains(j.Id)).ExecuteDeleteAsync();
        await db.LedgerAccounts.Where(a => customers.Contains(a.Id) || fundingIds.Contains(a.Id)).ExecuteDeleteAsync();
        await db.Wallets.Where(w => walletIds.Contains(w.Id)).ExecuteDeleteAsync();
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
    private sealed class FailedJournal
    {
        public Guid Id { get; set; }
        public Guid[] AccountIds { get; set; } = [];
    }
    private sealed class InvalidPostingRepository(LedgerlyDbContext db, FailedJournal observed) : IJournalEntryRepository
    {
        public void Add(JournalEntry journal)
        {
            new JournalEntryRepository(db).Add(journal);
            observed.Id = journal.Id;
            observed.AccountIds = journal.Postings.Select(p => p.AccountId).ToArray();
            // Test-only corruption, rejected by the real PostgreSQL FK during SaveChanges.
            db.Postings.Local.Single(p => p.JournalEntryId == journal.Id && p.Sequence == 1).AccountId = Guid.NewGuid();
        }
        public Task<JournalEntrySnapshot?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            new JournalEntryRepository(db).GetByIdAsync(id, cancellationToken);
    }
}
