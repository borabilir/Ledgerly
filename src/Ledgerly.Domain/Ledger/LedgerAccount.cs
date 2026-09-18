using Ledgerly.Domain.Wallets;

namespace Ledgerly.Domain.Ledger;

public sealed class LedgerAccount
{
    private LedgerAccount(
        Guid? walletId,
        LedgerAccountType type,
        LedgerAccountPurpose purpose,
        Currency currency,
        DateTimeOffset createdAt
    )
    {
        Id = Guid.NewGuid();
        WalletId = walletId;
        Type = type;
        Purpose = purpose;
        Currency = currency;
        CreatedAtUtc = createdAt.ToUniversalTime();
    }

    public Guid Id { get; }

    public Guid? WalletId { get; }

    public LedgerAccountType Type { get; }

    public LedgerAccountPurpose Purpose { get; }

    public Currency Currency { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public static LedgerAccount CreateForWallet(Guid walletId, Currency currency, DateTimeOffset createdAt)
    {
        if (walletId == Guid.Empty)
        {
            throw new ArgumentException("Wallet ID cannot be empty.", nameof(walletId));
        }

        ArgumentNullException.ThrowIfNull(currency);

        return new LedgerAccount(walletId, LedgerAccountType.Liability, LedgerAccountPurpose.Wallet, currency, createdAt);
    }

    public static LedgerAccount CreateTestFunding(Currency currency, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(currency);

        return new LedgerAccount(null, LedgerAccountType.Asset, LedgerAccountPurpose.TestFunding, currency, createdAt);
    }
}
