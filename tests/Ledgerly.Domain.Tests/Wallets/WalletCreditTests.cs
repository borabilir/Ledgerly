using Ledgerly.Domain.Wallets;

namespace Ledgerly.Domain.Tests.Wallets;

public sealed class WalletCreditTests
{
    [Fact]
    public void Credit_ShouldAccumulateExactAmounts()
    {
        var wallet = NewWallet();
        wallet.Credit(100m);
        wallet.Credit(0.1234m);
        Assert.Equal(100.1234m, wallet.Balance);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.00001")]
    [InlineData("1000000000000000")]
    [InlineData("79228162514264337593543950335")]
    public void Credit_WhenAmountIsInvalid_ShouldRejectWithoutChangingBalance(string input)
    {
        var wallet = NewWallet();
        wallet.Credit(10m);
        var amount = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentOutOfRangeException>(() => wallet.Credit(amount));
        Assert.Equal(10m, wallet.Balance);
    }

    [Fact]
    public void Credit_WhenBalanceWouldOverflow_ShouldRejectWithoutChangingBalance()
    {
        var wallet = NewWallet();
        wallet.Credit(999_999_999_999_999.9998m);
        wallet.Credit(0.0001m);
        Assert.Throws<ArgumentOutOfRangeException>(() => wallet.Credit(0.0001m));
        Assert.Equal(999_999_999_999_999.9999m, wallet.Balance);
    }

    private static Wallet NewWallet() => Wallet.Create(Guid.NewGuid(), Currency.FromCode("TRY"), DateTimeOffset.UtcNow);
}
