using Ledgerly.Domain.Ledger;
using Ledgerly.Domain.Wallets;

namespace Ledgerly.Application.Ledger;

public interface ILedgerAccountRepository
{
    void Add(LedgerAccount account);

    // Read models are snapshots, not tracked aggregates for later mutation.
    Task<LedgerAccountSnapshot?> GetByWalletIdAsync(Guid walletId, CancellationToken cancellationToken = default);

    Task<LedgerAccountSnapshot?> GetTestFundingAsync(Currency currency, CancellationToken cancellationToken = default);
}
