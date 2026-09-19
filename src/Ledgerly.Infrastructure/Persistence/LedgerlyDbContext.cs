using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Application.Ledger;
using Ledgerly.Application.Wallets.CreateWallet;
using Ledgerly.Application.Wallets.TestDeposit;
using Ledgerly.Application.Wallets.TransferWallet;
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
    internal DbSet<TestDepositOperationRecord> TestDepositOperations => Set<TestDepositOperationRecord>();
    internal DbSet<WalletTransferRecord> WalletTransfers => Set<WalletTransferRecord>();

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
                ConstraintName: WalletTransferConfiguration.IdempotencyUniqueIndexName,
            })
        {
            throw new TransferIdempotencyWriteConflictException(exception);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: TestDepositOperationConfiguration.PrimaryKeyName,
            })
        {
            throw new TestDepositIdempotencyWriteConflictException(exception);
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
        catch (Exception exception) when (IsDeadlock(exception))
        {
            // PostgreSQL can choose either writer as the deadlock victim when both
            // stage new ledger rows. The entire SaveChanges transaction is rolled back.
            throw new LedgerWriteConflictException(exception);
        }
    }

    private static bool IsDeadlock(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected })
            {
                return true;
            }
        }

        return false;
    }
}
