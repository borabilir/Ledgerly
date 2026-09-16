using Ledgerly.Domain.Wallets;
using Ledgerly.Domain.Ledger;

namespace Ledgerly.Infrastructure.Persistence.Records;

internal sealed class LedgerAccountRecord
{
    public Guid Id { get; set; }
    public Guid? WalletId { get; set; }
    public LedgerAccountType Type { get; set; }
    public Currency Currency { get; set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
