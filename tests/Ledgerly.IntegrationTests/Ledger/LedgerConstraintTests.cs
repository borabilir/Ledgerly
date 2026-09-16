using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Domain.Ledger;
using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Ledger;
using Ledgerly.Infrastructure.Persistence;
using Ledgerly.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ledgerly.IntegrationTests.Ledger;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class LedgerConstraintTests(PostgresFixture fixture)
{
    [Theory]
    [InlineData(false, "ux_ledger_accounts_wallet_id")]
    [InlineData(true, "ux_ledger_accounts_test_funding_currency")]
    public async Task SaveChanges_WhenAccountDuplicates_ShouldPreserveSpecificDatabaseError(bool funding, string constraint)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var currency = Currency.FromCode("TRY");
            var now = PostgresFixture.FixedUtcNow;
            var wallet = Wallet.Create(Guid.NewGuid(), currency, now);
            db.Wallets.Add(wallet);
            var accounts = new LedgerAccountRepository(db);
            accounts.Add(funding ? LedgerAccount.CreateTestFunding(currency, now)
                : LedgerAccount.CreateForWallet(wallet.Id, currency, now));
            await ((IUnitOfWork)db).SaveChangesAsync();
            db.ChangeTracker.Clear();
            accounts.Add(funding ? LedgerAccount.CreateTestFunding(currency, now)
                : LedgerAccount.CreateForWallet(wallet.Id, currency, now));

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => ((IUnitOfWork)db).SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
            Assert.Equal(constraint, postgres.ConstraintName);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Theory]
    [InlineData("missing-wallet", "23503", "fk_ledger_accounts_wallet_currency")]
    [InlineData("wallet-currency", "23503", "fk_ledger_accounts_wallet_currency")]
    [InlineData("asset-with-wallet", "23514", "ck_ledger_accounts_type_wallet")]
    [InlineData("liability-without-wallet", "23514", "ck_ledger_accounts_type_wallet")]
    [InlineData("unknown-type", "23514", "ck_ledger_accounts_type_wallet")]
    [InlineData("missing-account", "23503", "fk_postings_account_currency")]
    [InlineData("account-currency", "23503", "fk_postings_account_currency")]
    [InlineData("journal-currency", "23503", "fk_postings_journal_currency")]
    [InlineData("missing-journal", "23503", "fk_postings_journal_currency")]
    [InlineData("zero-amount", "23514", "ck_postings_amount_positive")]
    [InlineData("negative-amount", "23514", "ck_postings_amount_positive")]
    [InlineData("unknown-direction", "23514", "ck_postings_direction")]
    [InlineData("negative-sequence", "23514", "ck_postings_sequence")]
    [InlineData("duplicate-sequence", "23505", "pk_postings")]
    [InlineData("delete-wallet", "23001", "fk_ledger_accounts_wallet_currency")]
    [InlineData("delete-account", "23001", "fk_postings_account_currency")]
    [InlineData("delete-journal", "23001", "fk_postings_journal_currency")]
    public async Task Database_WhenInvalidStateBypassesDomain_ShouldRejectIt(string scenario, string sqlState, string constraint)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var currency = Currency.FromCode("TRY");
            var now = PostgresFixture.FixedUtcNow;
            var wallet = Wallet.Create(Guid.NewGuid(), currency, now);
            var unlinkedWallet = Wallet.Create(Guid.NewGuid(), currency, now);
            var customer = LedgerAccount.CreateForWallet(wallet.Id, currency, now);
            var funding = LedgerAccount.CreateTestFunding(currency, now);
            var journal = JournalEntry.Create(currency,
                [Posting.Create(funding.Id, PostingDirection.Debit, 10m),
                 Posting.Create(customer.Id, PostingDirection.Credit, 10m)], now);
            db.Wallets.AddRange(wallet, unlinkedWallet);
            var accounts = new LedgerAccountRepository(db);
            accounts.Add(customer);
            accounts.Add(funding);
            new JournalEntryRepository(db).Add(journal);
            await ((IUnitOfWork)db).SaveChangesAsync();

            var unknownId = Guid.NewGuid();
            var otherCurrencyAccount = Guid.NewGuid();
            // Only a database test fixture, not USD support in the domain/API.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO ledger_accounts (id, wallet_id, type, currency, created_at_utc)
                VALUES ({otherCurrencyAccount}, NULL, 1, 'USD', {now})
                """);

            Task<int> InvalidWrite() => scenario switch
            {
                "missing-wallet" => db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO ledger_accounts (id, wallet_id, type, currency, created_at_utc) VALUES ({unknownId}, {Guid.NewGuid()}, 2, 'TRY', {now})
                    """),
                "wallet-currency" => db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO ledger_accounts (id, wallet_id, type, currency, created_at_utc) VALUES ({unknownId}, {unlinkedWallet.Id}, 2, 'USD', {now})
                    """),
                "asset-with-wallet" => db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO ledger_accounts (id, wallet_id, type, currency, created_at_utc) VALUES ({unknownId}, {wallet.Id}, 1, 'TRY', {now})
                    """),
                "liability-without-wallet" => db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO ledger_accounts (id, wallet_id, type, currency, created_at_utc) VALUES ({unknownId}, NULL, 2, 'TRY', {now})
                    """),
                "unknown-type" => db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO ledger_accounts (id, wallet_id, type, currency, created_at_utc) VALUES ({unknownId}, NULL, 3, 'TRY', {now})
                    """),
                "delete-wallet" => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM wallets WHERE id = {wallet.Id}"),
                "delete-account" => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ledger_accounts WHERE id = {customer.Id}"),
                "delete-journal" => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM journal_entries WHERE id = {journal.Id}"),
                _ => InsertInvalidPosting(),
            };

            Task<int> InsertInvalidPosting()
            {
                var accountId = scenario == "missing-account" ? unknownId
                    : scenario is "account-currency" or "journal-currency" ? otherCurrencyAccount : customer.Id;
                var journalId = scenario == "missing-journal" ? unknownId : journal.Id;
                var code = scenario == "journal-currency" ? "USD" : "TRY";
                var amount = scenario == "zero-amount" ? 0m : scenario == "negative-amount" ? -1m : 1m;
                var direction = scenario == "unknown-direction" ? 0 : 1;
                var sequence = scenario == "negative-sequence" ? -1 : scenario == "duplicate-sequence" ? 0 : 2;
                return db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO postings (journal_entry_id, sequence, account_id, currency, direction, amount)
                    VALUES ({journalId}, {sequence}, {accountId}, {code}, {direction}, {amount})
                    """);
            }

            var exception = await Assert.ThrowsAsync<PostgresException>(InvalidWrite);
            Assert.Equal(sqlState, exception.SqlState);
            Assert.Equal(constraint, exception.ConstraintName);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
