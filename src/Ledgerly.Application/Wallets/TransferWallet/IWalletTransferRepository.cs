namespace Ledgerly.Application.Wallets.TransferWallet;

public interface IWalletTransferRepository
{
    Task<WalletTransferSnapshot?> GetAsync(
        Guid sourceWalletId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    void Add(string idempotencyKey, TransferWalletResult result, DateTimeOffset createdAtUtc);
}

public sealed record WalletTransferSnapshot(TransferWalletResult Result);
