using System.Data.Common;
using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Domain.Ledger;
using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Ledger;
using Ledgerly.Infrastructure.Persistence;
using Ledgerly.Infrastructure.Persistence.Records;
using Ledgerly.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit.Abstractions;

namespace Ledgerly.IntegrationTests.Ledger;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Lab", "JournalAtomicity")]
public sealed class JournalAtomicityTests(PostgresFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public Task SeparateSaves_WhenSecondPostingFails_ShouldReproducePartialJournal() =>
        RunFailureScenarioAsync(saveSeparately: true);

    [Fact]
    public Task SingleUnitOfWork_WhenSecondPostingFails_ShouldRollBackEntireJournal() =>
        RunFailureScenarioAsync(saveSeparately: false);

    private async Task RunFailureScenarioAsync(bool saveSeparately)
    {
        await using var seedScope = fixture.Services.CreateAsyncScope();
        var seed = seedScope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
        var connectionString = seed.Database.GetConnectionString()!;
        var observerOptions = new DbContextOptionsBuilder<LedgerlyDbContext>().UseNpgsql(connectionString).Options;
        var commands = new SuccessfulInsertObserver();
        // Separate commands prove a preceding INSERT really executed before the failure.
        var writerOptions = new DbContextOptionsBuilder<LedgerlyDbContext>()
            .UseNpgsql(connectionString, options => options.MaxBatchSize(1))
            .AddInterceptors(commands).Options;
        var currency = Currency.FromCode("TRY");
        var now = PostgresFixture.FixedUtcNow;
        var wallet = Wallet.Create(Guid.NewGuid(), currency, now);
        var account = LedgerAccount.CreateForWallet(wallet.Id, currency, now);
        var missingAccountId = Guid.NewGuid();
        var journal = JournalEntry.Create(currency,
            [Posting.Create(account.Id, PostingDirection.Debit, 10m),
             Posting.Create(missingAccountId, PostingDirection.Credit, 10m)], now);

        try
        {
            seed.Wallets.Add(wallet);
            new LedgerAccountRepository(seed).Add(account);
            await ((IUnitOfWork)seed).SaveChangesAsync();

            await using var writer = new LedgerlyDbContext(writerOptions);
            DbUpdateException exception;
            if (saveSeparately)
            {
                // Deliberately broken persistence workflow, retained only as a lab reproduction.
                writer.JournalEntries.Add(new JournalEntryRecord
                {
                    Id = journal.Id, Currency = currency, CreatedAtUtc = now,
                });
                await writer.SaveChangesAsync();
                writer.Postings.Add(ToRecord(journal, 0));
                await writer.SaveChangesAsync();
                writer.Postings.Add(ToRecord(journal, 1));
                exception = await Assert.ThrowsAsync<DbUpdateException>(() => writer.SaveChangesAsync());
            }
            else
            {
                new JournalEntryRepository(writer).Add(journal);
                exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
                    ((IUnitOfWork)writer).SaveChangesAsync());
            }

            var postgres = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgres.SqlState);
            Assert.Equal("fk_postings_account_currency", postgres.ConstraintName);
            Assert.Contains(commands.Tables, name => name == "journal_entries");
            Assert.Contains(commands.Tables, name => name == "postings");

            // No outer test transaction: a fresh connection observes the committed outcome.
            await using var observer = new LedgerlyDbContext(observerOptions);
            var journalCount = await observer.JournalEntries.CountAsync(value => value.Id == journal.Id);
            var postingCount = await observer.Postings.CountAsync(value => value.JournalEntryId == journal.Id);
            var expectedCount = saveSeparately ? 1 : 0;
            Assert.Equal(expectedCount, journalCount);
            Assert.Equal(expectedCount, postingCount);
            Assert.True(await observer.LedgerAccounts.AnyAsync(value => value.Id == account.Id));
            Assert.Equal(0m, await observer.Wallets.Where(value => value.Id == wallet.Id)
                .Select(value => value.Balance).SingleAsync());
            output.WriteLine($"Mode: {(saveSeparately ? "separate saves" : "single unit of work")}");
            output.WriteLine($"Successful INSERT commands: {string.Join(", ", commands.Tables)}");
            output.WriteLine($"SQLSTATE: {postgres.SqlState}; constraint: {postgres.ConstraintName}");
            output.WriteLine($"Fresh connection: journals={journalCount}, postings={postingCount}");
        }
        finally
        {
            await using var cleanup = new LedgerlyDbContext(observerOptions);
            await cleanup.Postings.Where(value => value.JournalEntryId == journal.Id).ExecuteDeleteAsync();
            await cleanup.JournalEntries.Where(value => value.Id == journal.Id).ExecuteDeleteAsync();
            await cleanup.LedgerAccounts.Where(value => value.Id == account.Id).ExecuteDeleteAsync();
            await cleanup.Wallets.Where(value => value.Id == wallet.Id).ExecuteDeleteAsync();
        }
    }

    private static PostingRecord ToRecord(JournalEntry journal, int index) => new()
    {
        JournalEntryId = journal.Id,
        Sequence = index,
        AccountId = journal.Postings[index].AccountId,
        Currency = journal.Currency,
        Direction = journal.Postings[index].Direction,
        Amount = journal.Postings[index].Amount,
    };

    private sealed class SuccessfulInsertObserver : DbCommandInterceptor
    {
        public List<string> Tables { get; } = [];

        private void Record(DbCommand command)
        {
            foreach (var table in new[] { "journal_entries", "postings" })
            {
                if (command.CommandText.TrimStart().StartsWith($"INSERT INTO {table}", StringComparison.Ordinal))
                {
                    Tables.Add(table);
                }
            }
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }
    }
}
