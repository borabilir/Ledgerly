using Ledgerly.Domain.Ledger;
using Ledgerly.Domain.Wallets;

namespace Ledgerly.Domain.Tests.Ledger;

public sealed class JournalEntryTests
{
    private static readonly Currency Try = Currency.FromCode("TRY");
    private static readonly DateTimeOffset Now =
        new(2026, 9, 15, 15, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void Create_WhenBalanced_ShouldCreateJournalWithUtcTime()
    {
        var debit = Posting.Create(Guid.NewGuid(), PostingDirection.Debit, 100m);
        var credit = Posting.Create(Guid.NewGuid(), PostingDirection.Credit, 100m);

        var entry = JournalEntry.Create(Try, [debit, credit], Now);

        Assert.NotEqual(Guid.Empty, entry.Id);
        Assert.Equal(Try, entry.Currency);
        Assert.Equal(Now.ToUniversalTime(), entry.CreatedAtUtc);
        Assert.Equal(TimeSpan.Zero, entry.CreatedAtUtc.Offset);
        Assert.Equal(new[] { debit, credit }, entry.Postings);
    }

    [Fact]
    public void Create_WhenMultipleCreditsBalanceOneDebit_ShouldAcceptAllPostings()
    {
        Posting[] postings = [
            Posting.Create(Guid.NewGuid(), PostingDirection.Debit, 100m),
            Posting.Create(Guid.NewGuid(), PostingDirection.Credit, 60m),
            Posting.Create(Guid.NewGuid(), PostingDirection.Credit, 40m),
        ];

        var entry = JournalEntry.Create(Try, postings, Now);

        Assert.Equal(postings, entry.Postings);
    }

    [Fact]
    public void Create_WhenFractionalAmountsBalance_ShouldAcceptWithoutRounding()
    {
        Posting[] postings = [
            Posting.Create(Guid.NewGuid(), PostingDirection.Credit, 0.3m),
            Posting.Create(Guid.NewGuid(), PostingDirection.Debit, 0.1m),
            Posting.Create(Guid.NewGuid(), PostingDirection.Debit, 0.2m),
        ];

        var entry = JournalEntry.Create(Try, postings, Now);

        Assert.Equal(postings, entry.Postings);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(110)]
    public void Create_WhenDebitAndCreditDiffer_ShouldRejectJournal(int creditAmount)
    {
        Posting[] postings = [
            Posting.Create(Guid.NewGuid(), PostingDirection.Debit, 100m),
            Posting.Create(Guid.NewGuid(), PostingDirection.Credit, creditAmount),
        ];

        Assert.Throws<ArgumentException>(() => JournalEntry.Create(Try, postings, Now));
    }

    [Fact]
    public void Create_WhenDifferenceIsSmallestSupportedUnit_ShouldRejectJournal()
    {
        Posting[] postings = [
            Posting.Create(Guid.NewGuid(), PostingDirection.Debit, 1m),
            Posting.Create(Guid.NewGuid(), PostingDirection.Credit, 1.0001m),
        ];

        Assert.Throws<ArgumentException>(() => JournalEntry.Create(Try, postings, Now));
    }

    [Theory]
    [InlineData(PostingDirection.Debit)]
    [InlineData(PostingDirection.Credit)]
    public void Create_WhenAllPostingsHaveSameDirection_ShouldRejectJournal(PostingDirection direction)
    {
        Posting[] postings = [
            Posting.Create(Guid.NewGuid(), direction, 100m),
            Posting.Create(Guid.NewGuid(), direction, 100m),
        ];

        Assert.Throws<ArgumentException>(() => JournalEntry.Create(Try, postings, Now));
    }

    [Fact]
    public void Create_WhenBalancedOnSameAccountOnly_ShouldRejectNoOpJournal()
    {
        var accountId = Guid.NewGuid();
        Posting[] postings = [
            Posting.Create(accountId, PostingDirection.Debit, 100m),
            Posting.Create(accountId, PostingDirection.Credit, 100m),
        ];

        Assert.Throws<ArgumentException>(() => JournalEntry.Create(Try, postings, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Create_WhenFewerThanTwoPostings_ShouldRejectJournal(int count)
    {
        var postings = Enumerable.Range(0, count)
            .Select(_ => Posting.Create(Guid.NewGuid(), PostingDirection.Debit, 1m));

        Assert.Throws<ArgumentException>(() => JournalEntry.Create(Try, postings, Now));
    }

    [Fact]
    public void Create_WhenCurrencyIsNull_ShouldRejectJournal()
    {
        Assert.Throws<ArgumentNullException>(() => JournalEntry.Create(null!, BalancedPostings(), Now));
    }

    [Fact]
    public void Create_WhenPostingsAreNull_ShouldRejectJournal()
    {
        Assert.Throws<ArgumentNullException>(() => JournalEntry.Create(Try, null!, Now));
    }

    [Fact]
    public void Create_WhenPostingContainsNull_ShouldRejectJournal()
    {
        Posting[] postings = [
            Posting.Create(Guid.NewGuid(), PostingDirection.Debit, 1m),
            null!,
        ];

        Assert.Throws<ArgumentException>(() => JournalEntry.Create(Try, postings, Now));
    }

    [Fact]
    public void Create_WhenCallerChangesInputCollection_ShouldKeepOriginalBalancedPostings()
    {
        var postings = BalancedPostings();
        var originalDebit = postings[0];
        var entry = JournalEntry.Create(Try, postings, Now);

        postings[0] = Posting.Create(Guid.NewGuid(), PostingDirection.Debit, 999m);

        Assert.Equal(originalDebit, entry.Postings[0]);
        var exposedList = Assert.IsAssignableFrom<IList<Posting>>(entry.Postings);
        Assert.Throws<NotSupportedException>(() => exposedList[0] = postings[0]);
        Assert.Throws<NotSupportedException>(() => exposedList.Clear());
        Assert.Equal(2, entry.Postings.Count);
    }

    private static Posting[] BalancedPostings() => [
        Posting.Create(Guid.NewGuid(), PostingDirection.Debit, 1m),
        Posting.Create(Guid.NewGuid(), PostingDirection.Credit, 1m),
    ];
}
