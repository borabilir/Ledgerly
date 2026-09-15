namespace Ledgerly.Domain.Ledger;

public sealed record Posting
{
    private const decimal MaximumAmount = 999_999_999_999_999.9999m;

    private Posting(Guid accountId, PostingDirection direction, decimal amount)
    {
        AccountId = accountId;
        Direction = direction;
        Amount = amount;
    }

    public Guid AccountId { get; }

    public PostingDirection Direction { get; }

    public decimal Amount { get; }

    public static Posting Create(Guid accountId, PostingDirection direction, decimal amount)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Account ID cannot be empty.", nameof(accountId));
        }

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), "Posting direction must be debit or credit.");
        }

        if (amount <= 0m || amount > MaximumAmount || decimal.Round(amount, 4) != amount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "Posting amount must be positive and fit within 19 digits with up to 4 decimal places."
            );
        }

        return new Posting(accountId, direction, amount);
    }
}
