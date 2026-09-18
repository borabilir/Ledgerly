using Ledgerly.Domain.Ledger;

namespace Ledgerly.Application.Ledger;

public sealed record LedgerAccountSnapshot(
    Guid Id,
    Guid? WalletId,
    LedgerAccountType Type,
    LedgerAccountPurpose Purpose,
    string CurrencyCode,
    DateTimeOffset CreatedAtUtc
);
