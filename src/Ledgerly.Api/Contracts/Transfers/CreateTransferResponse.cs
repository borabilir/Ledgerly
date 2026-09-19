namespace Ledgerly.Api.Contracts.Transfers;

public sealed record CreateTransferResponse(
    Guid JournalEntryId,
    Guid SourceWalletId,
    Guid DestinationWalletId,
    string CurrencyCode,
    decimal Amount,
    decimal SourceBalance,
    decimal DestinationBalance);
