namespace Ledgerly.Infrastructure.Persistence.Records;

internal sealed class WalletTransferRecord
{
    public Guid Id { get; set; }
    public Guid SourceWalletId { get; set; }
    public Guid DestinationWalletId { get; set; }
    public string IdempotencyKey { get; set; } = null!;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = null!;
    public Guid JournalEntryId { get; set; }
    public decimal SourceBalance { get; set; }
    public decimal DestinationBalance { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
