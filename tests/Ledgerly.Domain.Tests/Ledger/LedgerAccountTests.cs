using Ledgerly.Domain.Ledger;
using Ledgerly.Domain.Wallets;

namespace Ledgerly.Domain.Tests.Ledger;

public sealed class LedgerAccountTests
{
    private static readonly Currency Try = Currency.FromCode("TRY");
    private static readonly DateTimeOffset Now =
        new(2026, 9, 16, 15, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void CreateForWallet_WhenValid_ShouldCreateLiabilityLinkedToWallet()
    {
        var walletId = Guid.NewGuid();

        var account = LedgerAccount.CreateForWallet(walletId, Try, Now);

        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.NotEqual(walletId, account.Id);
        Assert.Equal(walletId, account.WalletId);
        Assert.Equal(LedgerAccountType.Liability, account.Type);
        Assert.Equal(Try, account.Currency);
        Assert.Equal(Now.ToUniversalTime(), account.CreatedAtUtc);
        Assert.Equal(TimeSpan.Zero, account.CreatedAtUtc.Offset);
    }

    [Fact]
    public void CreateForWallet_WhenWalletIdIsEmpty_ShouldRejectAccount()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            LedgerAccount.CreateForWallet(Guid.Empty, Try, Now));

        Assert.Equal("walletId", exception.ParamName);
    }

    [Fact]
    public void CreateForWallet_WhenCurrencyIsNull_ShouldRejectAccount()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            LedgerAccount.CreateForWallet(Guid.NewGuid(), null!, Now));

        Assert.Equal("currency", exception.ParamName);
    }

    [Fact]
    public void CreateTestFunding_WhenValid_ShouldCreateAssetWithoutWallet()
    {
        var account = LedgerAccount.CreateTestFunding(Try, Now);

        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.Null(account.WalletId);
        Assert.Equal(LedgerAccountType.Asset, account.Type);
        Assert.Equal(Try, account.Currency);
        Assert.Equal(Now.ToUniversalTime(), account.CreatedAtUtc);
        Assert.Equal(TimeSpan.Zero, account.CreatedAtUtc.Offset);
    }

    [Fact]
    public void CreateTestFunding_WhenCurrencyIsNull_ShouldRejectAccount()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            LedgerAccount.CreateTestFunding(null!, Now));

        Assert.Equal("currency", exception.ParamName);
    }
}
