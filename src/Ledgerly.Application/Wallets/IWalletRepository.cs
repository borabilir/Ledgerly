using Ledgerly.Domain.Wallets;

namespace Ledgerly.Application.Wallets;

public interface IWalletRepository
{
    // Returns a read-only snapshot; changes to this instance are not tracked for saving.
    Task<Wallet?> GetByIdAsync(Guid walletId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Guid ownerId,
        Currency currency,
        CancellationToken cancellationToken = default
    );

    void Add(Wallet wallet);
}
