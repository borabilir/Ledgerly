using Ledgerly.Domain.Ledger;

namespace Ledgerly.Domain.Tests.Ledger;

public sealed class PostingTests
{
    [Theory]
    [InlineData(PostingDirection.Debit)]
    [InlineData(PostingDirection.Credit)]
    public void Create_WhenValuesAreValid_ShouldKeepAccountDirectionAndAmount(PostingDirection direction)
    {
        var accountId = Guid.NewGuid();

        var posting = Posting.Create(accountId, direction, 100.1234m);

        Assert.Equal(accountId, posting.AccountId);
        Assert.Equal(direction, posting.Direction);
        Assert.Equal(100.1234m, posting.Amount);
    }

    [Fact]
    public void Create_WhenAccountIdIsEmpty_ShouldRejectPosting()
    {
        Assert.Throws<ArgumentException>(() => Posting.Create(Guid.Empty, PostingDirection.Debit, 1m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void Create_WhenDirectionIsUndefined_ShouldRejectPosting(int direction)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Posting.Create(Guid.NewGuid(), (PostingDirection)direction, 1m));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.00001")]
    [InlineData("1.00001")]
    [InlineData("1000000000000000")]
    public void Create_WhenAmountCannotBeRecordedExactly_ShouldRejectPosting(string value)
    {
        var amount = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Posting.Create(Guid.NewGuid(), PostingDirection.Debit, amount));
    }

    [Theory]
    [InlineData("0.0001")]
    [InlineData("1.00000")]
    [InlineData("999999999999999.9999")]
    public void Create_WhenAmountIsOnSupportedBoundary_ShouldPreserveExactValue(string value)
    {
        var amount = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        var posting = Posting.Create(Guid.NewGuid(), PostingDirection.Credit, amount);

        Assert.Equal(amount, posting.Amount);
    }
}
