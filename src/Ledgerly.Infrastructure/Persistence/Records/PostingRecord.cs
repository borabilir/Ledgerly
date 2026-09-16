using Ledgerly.Domain.Wallets;
using Ledgerly.Domain.Ledger;

namespace Ledgerly.Infrastructure.Persistence.Records;

internal sealed class PostingRecord
{
    public Guid JournalEntryId { get; set; }
    public int Sequence { get; set; }
    public Guid AccountId { get; set; }
    public Currency Currency { get; set; } = null!;
    public PostingDirection Direction { get; set; }
    public decimal Amount { get; set; }
}
