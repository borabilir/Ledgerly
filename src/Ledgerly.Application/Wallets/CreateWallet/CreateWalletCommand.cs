namespace Ledgerly.Application.Wallets.CreateWallet;

public sealed record CreateWalletCommand(
    Guid OwnerId,
    string CurrencyCode
);
