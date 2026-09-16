using Ledgerly.Domain.Ledger;

namespace Ledgerly.Application.Ledger;

public sealed record LedgerAccountSnapshot(
    Guid Id,
    Guid? WalletId,
    LedgerAccountType Type,
    string CurrencyCode,
    DateTimeOffset CreatedAtUtc
);
