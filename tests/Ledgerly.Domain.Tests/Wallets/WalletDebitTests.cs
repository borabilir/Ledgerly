using System.Globalization;
using Ledgerly.Domain.Wallets;

namespace Ledgerly.Domain.Tests.Wallets;

public sealed class WalletDebitTests
{
    [Fact]
    public void Debit_WhenFundsAreSufficient_ShouldDecreaseBalanceExactly()
    {
        var wallet = FundedWallet(100.1234m);

        wallet.Debit(40.1234m);

        Assert.Equal(60m, wallet.Balance);
    }

    [Fact]
    public void Debit_WhenAmountEqualsBalance_ShouldLeaveZeroBalance()
    {
        var wallet = FundedWallet(100m);

        wallet.Debit(100m);

        Assert.Equal(0m, wallet.Balance);
    }

    [Fact]
    public void Debit_WhenFundsAreInsufficient_ShouldRejectWithoutChangingBalance()
    {
        var wallet = FundedWallet(100m);

        var exception = Assert.Throws<InsufficientFundsException>(() => wallet.Debit(100.0001m));

        Assert.Equal(100m, exception.AvailableBalance);
        Assert.Equal(100.0001m, exception.RequestedAmount);
        Assert.Equal(100m, wallet.Balance);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.00001")]
    [InlineData("1000000000000000")]
    public void Debit_WhenAmountIsInvalid_ShouldRejectWithoutChangingBalance(string input)
    {
        var wallet = FundedWallet(100m);
        var amount = decimal.Parse(input, CultureInfo.InvariantCulture);

        Assert.Throws<ArgumentOutOfRangeException>(() => wallet.Debit(amount));
        Assert.Equal(100m, wallet.Balance);
    }

    private static Wallet FundedWallet(decimal balance)
    {
        var wallet = Wallet.Create(Guid.NewGuid(), Currency.FromCode("TRY"), DateTimeOffset.UtcNow);
        wallet.Credit(balance);
        return wallet;
    }
}
