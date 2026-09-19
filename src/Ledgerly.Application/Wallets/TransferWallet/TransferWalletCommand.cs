namespace Ledgerly.Application.Wallets.TransferWallet;

public sealed record TransferWalletCommand(Guid SourceWalletId, Guid DestinationWalletId, decimal Amount);
