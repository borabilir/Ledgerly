using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Application.Ledger;
using Ledgerly.Application.Wallets.CreateWallet;
using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Persistence.Configurations;
using Ledgerly.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ledgerly.Infrastructure.Persistence;

public sealed class LedgerlyDbContext : DbContext, IUnitOfWork
{
    public LedgerlyDbContext(DbContextOptions<LedgerlyDbContext> options)
        : base(options)
    {
    }

    public DbSet<Wallet> Wallets => Set<Wallet>();
    internal DbSet<LedgerAccountRecord> LedgerAccounts => Set<LedgerAccountRecord>();
    internal DbSet<JournalEntryRecord> JournalEntries => Set<JournalEntryRecord>();
    internal DbSet<PostingRecord> Postings => Set<PostingRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LedgerlyDbContext).Assembly);
    }

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception) when (
            exception.Entries.Count > 0 && exception.Entries.All(entry => entry.Entity is Wallet))
        {
            throw new LedgerWriteConflictException(exception);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: LedgerAccountConfiguration.WalletUniqueIndexName
                    or LedgerAccountConfiguration.TestFundingUniqueIndexName,
            })
        {
            throw new LedgerWriteConflictException(exception);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: WalletConfiguration.OwnerCurrencyUniqueIndexName,
            }
            && exception.Entries.Count == 1
            && exception.Entries[0].State == EntityState.Added
            && exception.Entries[0].Entity is Wallet wallet
        )
        {
            // Create Wallet saves one new aggregate. Preserve ambiguous batch failures.
            throw new WalletAlreadyExistsException(wallet.OwnerId, wallet.Currency.Code, exception);
        }
    }
}
