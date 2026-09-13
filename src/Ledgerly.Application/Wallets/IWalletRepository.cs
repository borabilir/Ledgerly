using Ledgerly.Domain.Wallets;

namespace Ledgerly.Application.Wallets;

public interface IWalletRepository
{
    Task<bool> ExistsAsync(
        Guid ownerId,
        Currency currency,
        CancellationToken cancellationToken = default
    );

    void Add(Wallet wallet);
}
