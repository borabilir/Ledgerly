using Ledgerly.Domain.Wallets;

namespace Ledgerly.Application.Wallets.GetWallet;

public sealed record GetWalletResult(
    Guid WalletId,
    Guid OwnerId,
    string CurrencyCode,
    WalletStatus Status,
    decimal Balance,
    DateTimeOffset CreatedAtUtc
);
