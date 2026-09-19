namespace Ledgerly.Application.Wallets.TransferWallet;

public sealed record TransferCompletedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    Guid TransferId,
    Guid SourceWalletId,
    Guid DestinationWalletId,
    decimal Amount,
    string CurrencyCode)
{
    public const string EventType = "wallet.transfer-completed.v1";
}
