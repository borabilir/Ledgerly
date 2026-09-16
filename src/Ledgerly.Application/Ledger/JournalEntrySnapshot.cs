using Ledgerly.Domain.Ledger;

namespace Ledgerly.Application.Ledger;

public sealed record JournalEntrySnapshot(
    Guid Id,
    string CurrencyCode,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<Posting> Postings
);
