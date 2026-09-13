using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Domain.Wallets;
using Microsoft.EntityFrameworkCore;

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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LedgerlyDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        await base.SaveChangesAsync(cancellationToken);
    }
}
