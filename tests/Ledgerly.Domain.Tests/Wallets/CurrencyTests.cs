using Ledgerly.Domain.Wallets;

namespace Ledgerly.Domain.Tests.Wallets;

public sealed class CurrencyTests
{
    [Theory]
    [InlineData("TRY")]
    [InlineData("try")]
    [InlineData(" Try ")]
    public void FromCode_WhenCodeIsTry_ShouldReturnNormalizedTryCurrency(string code)
    {
        // Act
        var currency = Currency.FromCode(code);

        // Assert
        Assert.Equal("TRY", currency.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public void FromCode_WhenCodeIsBlank_ShouldThrowArgumentException(string code)
    {
        // Act
        var exception = Assert.Throws<ArgumentException>(() => Currency.FromCode(code));

        // Assert
        Assert.Equal("code", exception.ParamName);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData(" eur ")]
    public void FromCode_WhenCodeIsNotSupported_ShouldThrowArgumentException(string code)
    {
        // Act
        var exception = Assert.Throws<ArgumentException>(() => Currency.FromCode(code));

        // Assert
        Assert.Equal("code", exception.ParamName);
    }
}
