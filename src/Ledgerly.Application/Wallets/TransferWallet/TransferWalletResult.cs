namespace Ledgerly.Application.Wallets.TransferWallet;

public sealed record TransferWalletResult(
    Guid TransferId,
    Guid JournalEntryId,
    Guid SourceWalletId,
    Guid DestinationWalletId,
    string CurrencyCode,
    decimal Amount,
    decimal SourceBalance,
    decimal DestinationBalance);
