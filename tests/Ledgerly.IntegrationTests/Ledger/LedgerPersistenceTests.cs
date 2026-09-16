using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Application.Ledger;
using Ledgerly.Domain.Ledger;
using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Persistence;
using Ledgerly.Infrastructure.Persistence.Records;
using Ledgerly.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ledgerly.IntegrationTests.Ledger;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class LedgerPersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task SaveChanges_WhenCommitted_ShouldBeVisibleFromAnotherScope()
    {
        var currency = Currency.FromCode("TRY");
        var now = PostgresFixture.FixedUtcNow;
        var sender = Wallet.Create(Guid.NewGuid(), currency, now);
        var receiver = Wallet.Create(Guid.NewGuid(), currency, now);
        var senderAccount = LedgerAccount.CreateForWallet(sender.Id, currency, now);
        var receiverAccount = LedgerAccount.CreateForWallet(receiver.Id, currency, now);
        var journal = JournalEntry.Create(currency,
            [Posting.Create(senderAccount.Id, PostingDirection.Debit, 25m),
             Posting.Create(receiverAccount.Id, PostingDirection.Credit, 25m)], now);
        try
        {
            await using (var scope = fixture.Services.CreateAsyncScope())
            {
                var services = scope.ServiceProvider;
                var db = services.GetRequiredService<LedgerlyDbContext>();
                var accounts = services.GetRequiredService<ILedgerAccountRepository>();
                db.Wallets.AddRange(sender, receiver);
                accounts.Add(senderAccount);
                accounts.Add(receiverAccount);
                services.GetRequiredService<IJournalEntryRepository>().Add(journal);

                // Add only stages changes; nothing is visible to another connection yet.
                await using var beforeScope = fixture.Services.CreateAsyncScope();
                Assert.Null(await beforeScope.ServiceProvider.GetRequiredService<IJournalEntryRepository>()
                    .GetByIdAsync(journal.Id));
                await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            }

            await using var observerScope = fixture.Services.CreateAsyncScope();
            var observer = observerScope.ServiceProvider;
            var saved = await observer.GetRequiredService<IJournalEntryRepository>().GetByIdAsync(journal.Id);
            Assert.NotNull(saved);
            Assert.Equal(journal.Postings.ToArray(), saved.Postings.ToArray());
            Assert.Equal(senderAccount.Id, (await observer.GetRequiredService<ILedgerAccountRepository>()
                .GetByWalletIdAsync(sender.Id))!.Id);
            Assert.Equal(receiverAccount.Id, (await observer.GetRequiredService<ILedgerAccountRepository>()
                .GetByWalletIdAsync(receiver.Id))!.Id);
        }
        finally
        {
            await using var cleanupScope = fixture.Services.CreateAsyncScope();
            var db = cleanupScope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
            await db.Set<PostingRecord>().Where(value => value.JournalEntryId == journal.Id).ExecuteDeleteAsync();
            await db.Set<JournalEntryRecord>().Where(value => value.Id == journal.Id).ExecuteDeleteAsync();
            await db.Set<LedgerAccountRecord>().Where(value => value.Id == senderAccount.Id || value.Id == receiverAccount.Id)
                .ExecuteDeleteAsync();
            await db.Wallets.Where(value => value.Id == sender.Id || value.Id == receiver.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task SaveChanges_WhenJournalIsValid_ShouldPersistAndReadEveryPostingWithoutTracking()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<LedgerlyDbContext>();
        var accounts = services.GetRequiredService<ILedgerAccountRepository>();
        var journals = services.GetRequiredService<IJournalEntryRepository>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var currency = Currency.FromCode("TRY");
            var now = PostgresFixture.FixedUtcNow;
            var wallet = Wallet.Create(Guid.NewGuid(), currency, now);
            var customer = LedgerAccount.CreateForWallet(wallet.Id, currency, now);
            var funding = LedgerAccount.CreateTestFunding(currency, now);
            // The same immutable value can occur twice; each occurrence is a separate row.
            var debit = Posting.Create(funding.Id, PostingDirection.Debit, 1.2345m);
            var journal = JournalEntry.Create(currency,
                [debit, debit, Posting.Create(customer.Id, PostingDirection.Credit, 2.4690m)], now);

            db.Wallets.Add(wallet);
            accounts.Add(customer);
            accounts.Add(funding);
            journals.Add(journal);

            await unitOfWork.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var savedCustomer = await accounts.GetByWalletIdAsync(wallet.Id);
            var savedFunding = await accounts.GetTestFundingAsync(currency);
            var savedJournal = await journals.GetByIdAsync(journal.Id);

            Assert.NotNull(savedCustomer);
            Assert.Equal(customer.Id, savedCustomer.Id);
            Assert.Equal(wallet.Id, savedCustomer.WalletId);
            Assert.Equal(LedgerAccountType.Liability, savedCustomer.Type);
            Assert.Equal("TRY", savedCustomer.CurrencyCode);
            Assert.Equal(now, savedCustomer.CreatedAtUtc);
            Assert.NotNull(savedFunding);
            Assert.Equal(funding.Id, savedFunding.Id);
            Assert.Null(savedFunding.WalletId);
            Assert.Equal(LedgerAccountType.Asset, savedFunding.Type);
            Assert.NotNull(savedJournal);
            Assert.Equal(journal.Id, savedJournal.Id);
            Assert.Equal("TRY", savedJournal.CurrencyCode);
            Assert.Equal(now, savedJournal.CreatedAtUtc);
            Assert.Equal(journal.Postings.ToArray(), savedJournal.Postings.ToArray());
            Assert.Throws<NotSupportedException>(() => ((IList<Posting>)savedJournal.Postings).Clear());
            Assert.Empty(db.ChangeTracker.Entries());
            Assert.Equal(0m, await db.Wallets.Where(value => value.Id == wallet.Id)
                .Select(value => value.Balance).SingleAsync());
            Assert.Null(await accounts.GetByWalletIdAsync(Guid.NewGuid()));
            Assert.Null(await journals.GetByIdAsync(Guid.NewGuid()));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
