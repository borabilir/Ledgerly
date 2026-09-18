using Ledgerly.Application.Wallets;
using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ledgerly.Infrastructure.Wallets;

internal sealed class WalletRepository : IWalletRepository
{
    private readonly LedgerlyDbContext _dbContext;

    public WalletRepository(LedgerlyDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public Task<Wallet?> GetByIdAsync(Guid walletId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Wallets
            .AsNoTracking()
            .SingleOrDefaultAsync(wallet => wallet.Id == walletId, cancellationToken);
    }

    public Task<Wallet?> GetForUpdateAsync(Guid walletId, CancellationToken cancellationToken = default) =>
        _dbContext.Wallets.SingleOrDefaultAsync(wallet => wallet.Id == walletId, cancellationToken);

    public Task<bool> ExistsAsync(
        Guid ownerId,
        Currency currency,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(currency);

        return _dbContext.Wallets.AnyAsync(
            wallet => wallet.OwnerId == ownerId && wallet.Currency == currency,
            cancellationToken
        );
    }

    public void Add(Wallet wallet)
    {
        ArgumentNullException.ThrowIfNull(wallet);

        _dbContext.Wallets.Add(wallet);
    }
}
