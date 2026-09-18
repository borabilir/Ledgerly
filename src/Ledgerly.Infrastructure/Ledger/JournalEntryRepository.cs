using Ledgerly.Application.Ledger;
using Ledgerly.Domain.Ledger;
using Ledgerly.Infrastructure.Persistence;
using Ledgerly.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace Ledgerly.Infrastructure.Ledger;

internal sealed class JournalEntryRepository(LedgerlyDbContext dbContext) : IJournalEntryRepository
{
    public void Add(JournalEntry journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        dbContext.JournalEntries.Add(new JournalEntryRecord
        {
            Id = journal.Id,
            Currency = journal.Currency,
            CreatedAtUtc = journal.CreatedAtUtc,
            Postings = journal.Postings.Select((posting, index) => new PostingRecord
            {
                JournalEntryId = journal.Id,
                Sequence = index,
                AccountId = posting.AccountId,
                Currency = journal.Currency,
                Direction = posting.Direction,
                Amount = posting.Amount,
            }).ToList(),
        });
    }

    public async Task<JournalEntrySnapshot?> GetByIdAsync(Guid journalId, CancellationToken cancellationToken = default)
    {
        var record = await dbContext.JournalEntries.AsNoTracking()
            .Include(journal => journal.Postings)
            .SingleOrDefaultAsync(journal => journal.Id == journalId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        var postings = record.Postings.OrderBy(posting => posting.Sequence)
            .Select(posting => Posting.Create(posting.AccountId, posting.Direction, posting.Amount)).ToArray();
        return new JournalEntrySnapshot(record.Id, record.Currency.Code, record.CreatedAtUtc, Array.AsReadOnly(postings));
    }
}
