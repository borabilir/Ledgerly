namespace Ledgerly.Infrastructure.Persistence.Records;

internal sealed class TestDepositOperationRecord
{
    public Guid WalletId { get; set; }
    public string Key { get; set; } = null!;
    public decimal Amount { get; set; }
    public Guid JournalEntryId { get; set; }
    public string CurrencyCode { get; set; } = null!;
    public decimal Balance { get; set; }
}
