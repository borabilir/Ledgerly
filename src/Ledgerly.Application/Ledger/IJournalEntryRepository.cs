using Ledgerly.Domain.Ledger;

namespace Ledgerly.Application.Ledger;

public interface IJournalEntryRepository
{
    // Stages the entire journal. IUnitOfWork commits it together with other changes.
    void Add(JournalEntry journal);

    Task<JournalEntrySnapshot?> GetByIdAsync(Guid journalId, CancellationToken cancellationToken = default);
}
