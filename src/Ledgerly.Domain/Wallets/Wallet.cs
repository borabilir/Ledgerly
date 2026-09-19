namespace Ledgerly.Domain.Wallets;

public sealed class Wallet
{
    private const decimal MaximumBalance = 999_999_999_999_999.9999m;

    private Wallet(
        Guid id,
        Guid ownerId,
        Currency currency,
        WalletStatus status,
        decimal balance,
        DateTimeOffset createdAtUtc
    )
    {
        Id = id;
        OwnerId = ownerId;
        Currency = currency;
        Status = status;
        Balance = balance;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public Currency Currency { get; private set; }

    public WalletStatus Status { get; private set; }

    public decimal Balance { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public void Credit(decimal amount)
    {
        EnsureValidAmount(amount);

        if (Balance > MaximumBalance - amount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "The resulting wallet balance exceeds the supported limit.");
        }

        Balance += amount;
    }

    public void Debit(decimal amount)
    {
        EnsureValidAmount(amount);

        if (Balance < amount)
        {
            throw new InsufficientFundsException(Balance, amount);
        }

        Balance -= amount;
    }

    private static void EnsureValidAmount(decimal amount)
    {
        if (amount <= 0m || amount > MaximumBalance || decimal.Round(amount, 4) != amount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount),
                "Amount must be positive and fit within 19 digits with up to 4 decimal places.");
        }
    }

    public static Wallet Create(Guid ownerId, Currency currency, DateTimeOffset createdAt)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("Owner ID cannot be empty.", nameof(ownerId));
        }

        ArgumentNullException.ThrowIfNull(currency);

        var createdAtUtc = createdAt.ToUniversalTime();

        return new Wallet(
            id: Guid.NewGuid(),
            ownerId: ownerId,
            currency: currency,
            status: WalletStatus.Active,
            balance: 0m,
            createdAtUtc: createdAtUtc
        );
    }
}
