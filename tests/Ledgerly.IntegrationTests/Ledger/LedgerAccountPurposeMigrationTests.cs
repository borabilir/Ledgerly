using Ledgerly.Domain.Ledger;
using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Ledger;
using Ledgerly.Infrastructure.Persistence;
using Ledgerly.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ledgerly.IntegrationTests.Ledger;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class LedgerAccountPurposeMigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Migration_ShouldBackfillBothRolesAndPreserveExistingLedgerAcrossDownAndUp()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var shared = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
        // A unique schema keeps the shared test database and its migration history untouched.
        var schema = "purpose_migration_" + Guid.NewGuid().ToString("N");
        var builder = new NpgsqlConnectionStringBuilder(shared.Database.GetConnectionString())
        { SearchPath = schema, Pooling = false };
        using var identifiers = new NpgsqlCommandBuilder();
        var quotedSchema = identifiers.QuoteIdentifier(schema);
        await using var schemaConnection = new NpgsqlConnection(shared.Database.GetConnectionString());
        await schemaConnection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA {quotedSchema}", schemaConnection))
        {
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            var options = new DbContextOptionsBuilder<LedgerlyDbContext>().UseNpgsql(builder.ConnectionString).Options;
            await using var db = new LedgerlyDbContext(options);
            var migrator = db.GetService<IMigrator>();
            const string previous = "20260916043310_GuardWalletBalanceConcurrency";
            const string current = "20260917155805_AddLedgerAccountPurpose";
            await migrator.MigrateAsync(previous);
            var wallet = Guid.NewGuid();
            var owner = Guid.NewGuid();
            var customer = Guid.NewGuid();
            var funding = Guid.NewGuid();
            var journal = Guid.NewGuid();
            var now = PostgresFixture.FixedUtcNow;
            // Seed the previous schema: there is deliberately no purpose column yet.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO wallets (id, owner_id, currency, status, balance, created_at_utc)
                VALUES ({wallet}, {owner}, 'TRY', 1, 123.4567, {now});
                INSERT INTO ledger_accounts (id, wallet_id, type, currency, created_at_utc)
                VALUES ({customer}, {wallet}, 2, 'TRY', {now}), ({funding}, NULL, 1, 'TRY', {now});
                INSERT INTO journal_entries (id, currency, created_at_utc) VALUES ({journal}, 'TRY', {now});
                INSERT INTO postings (journal_entry_id, sequence, account_id, currency, direction, amount)
                VALUES ({journal}, 0, {funding}, 'TRY', 1, 123.4567), ({journal}, 1, {customer}, 'TRY', 2, 123.4567);
                """);

            await migrator.MigrateAsync(current);
            await AssertMigrated();
            await migrator.MigrateAsync(previous);
            Assert.Equal(2, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM ledger_accounts").SingleAsync());
            Assert.Equal(2, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM postings").SingleAsync());
            await migrator.MigrateAsync(current);
            await AssertMigrated();

            async Task AssertMigrated()
            {
                var accounts = new LedgerAccountRepository(db);
                var savedCustomer = await accounts.GetByWalletIdAsync(wallet);
                var savedFunding = await accounts.GetTestFundingAsync(Currency.FromCode("TRY"));
                Assert.NotNull(savedCustomer);
                Assert.NotNull(savedFunding);
                Assert.Equal(customer, savedCustomer.Id);
                Assert.Equal(LedgerAccountPurpose.Wallet, savedCustomer.Purpose);
                Assert.Equal(funding, savedFunding.Id);
                Assert.Equal(LedgerAccountPurpose.TestFunding, savedFunding.Purpose);
                Assert.Equal(now, savedCustomer.CreatedAtUtc);
                var savedJournal = await new JournalEntryRepository(db).GetByIdAsync(journal);
                Assert.NotNull(savedJournal);
                Assert.Equal(journal, savedJournal.Id);
                Assert.Collection(savedJournal.Postings,
                    p => Assert.Equal(Posting.Create(funding, PostingDirection.Debit, 123.4567m), p),
                    p => Assert.Equal(Posting.Create(customer, PostingDirection.Credit, 123.4567m), p));
                Assert.Equal(123.4567m, await db.Wallets.Where(w => w.Id == wallet).Select(w => w.Balance).SingleAsync());
            }
        }
        finally
        {
            // schema consists only of the fixed prefix and a generated GUID, never user input.
            await using var drop = new NpgsqlCommand($"DROP SCHEMA {quotedSchema} CASCADE", schemaConnection);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
