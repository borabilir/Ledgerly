using Ledgerly.Domain.Wallets;

namespace Ledgerly.Domain.Tests.Wallets;

public sealed class WalletCreateTests
{
    [Fact]
    public void Create_WhenValuesAreValid_ShouldCreateActiveWalletWithZeroBalance()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var createdAtUtc = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

        var currency = Currency.FromCode("TRY");

        // Act
        var wallet = Wallet.Create(ownerId, currency, createdAtUtc);

        // Assert
        Assert.NotEqual(Guid.Empty, wallet.Id);
        Assert.Equal(ownerId, wallet.OwnerId);
        Assert.Equal(currency, wallet.Currency);
        Assert.Equal(WalletStatus.Active, wallet.Status);
        Assert.Equal(0m, wallet.Balance);
        Assert.Equal(createdAtUtc, wallet.CreatedAtUtc);
    }

    [Fact]
    public void Create_WhenOwnerIdIsEmpty_ShouldThrowArgumentException()
    {
        // Arrange
        var createdAtUtc = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var currency = Currency.FromCode("TRY");

        // Act
        var exception = Assert.Throws<ArgumentException>(
            () => Wallet.Create(Guid.Empty, currency, createdAtUtc)
        );

        // Assert
        Assert.Equal("ownerId", exception.ParamName);
    }

    [Fact]
    public void Create_WhenCurrencyIsNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var createdAtUtc = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

        // Act
        var exception = Assert.Throws<ArgumentNullException>(
            () => Wallet.Create(ownerId, null!, createdAtUtc)
        );

        // Assert
        Assert.Equal("currency", exception.ParamName);
    }

    [Fact]
    public void Create_WhenCreatedAtHasOffset_ShouldNormalizeCreatedAtToUtc()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var currency = Currency.FromCode("TRY");

        var createdAt = new DateTimeOffset(2026, 9, 4, 15, 0, 0, TimeSpan.FromHours(3));

        // Act
        var wallet = Wallet.Create(ownerId, currency, createdAt);

        // Assert
        Assert.Equal(createdAt.ToUniversalTime(), wallet.CreatedAtUtc);
        Assert.Equal(TimeSpan.Zero, wallet.CreatedAtUtc.Offset);
    }
}
