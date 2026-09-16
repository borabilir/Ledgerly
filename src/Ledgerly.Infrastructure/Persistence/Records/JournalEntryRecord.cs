using Ledgerly.Domain.Wallets;
namespace Ledgerly.Infrastructure.Persistence.Records;

internal sealed class JournalEntryRecord
{
    public Guid Id { get; set; }
    public Currency Currency { get; set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public List<PostingRecord> Postings { get; set; } = [];
}
