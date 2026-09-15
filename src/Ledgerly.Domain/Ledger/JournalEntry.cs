using Ledgerly.Domain.Wallets;

namespace Ledgerly.Domain.Ledger;

public sealed class JournalEntry
{
    private JournalEntry(Currency currency, Posting[] postings, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        Currency = currency;
        Postings = Array.AsReadOnly(postings);
        CreatedAtUtc = createdAt.ToUniversalTime();
    }

    public Guid Id { get; }

    public Currency Currency { get; }

    public IReadOnlyList<Posting> Postings { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public static JournalEntry Create(Currency currency, IEnumerable<Posting> postings, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(postings);

        var snapshot = postings.ToArray();

        if (snapshot.Length < 2 || snapshot.Any(posting => posting is null))
        {
            throw new ArgumentException("A journal entry requires at least two non-null postings.", nameof(postings));
        }

        if (snapshot.Select(posting => posting.AccountId).Distinct().Count() < 2)
        {
            throw new ArgumentException("A journal entry must affect at least two different accounts.", nameof(postings));
        }

        var debits = snapshot.Where(posting => posting.Direction == PostingDirection.Debit).Sum(posting => posting.Amount);
        var credits = snapshot.Where(posting => posting.Direction == PostingDirection.Credit).Sum(posting => posting.Amount);

        if (debits != credits)
        {
            throw new ArgumentException("Total debits must equal total credits.", nameof(postings));
        }

        return new JournalEntry(currency, snapshot, createdAt);
    }
}
