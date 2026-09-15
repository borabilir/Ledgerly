using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Application.Wallets.CreateWallet;
using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Persistence.Configurations;
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
