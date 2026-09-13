using Ledgerly.Application.Wallets.CreateWallet;
using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Persistence;
using Ledgerly.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ledgerly.IntegrationTests.Wallets;

[Collection(PostgresCollection.Name)]
public sealed class CreateWalletPersistenceTests
{
    private readonly PostgresFixture _fixture;

    public CreateWalletPersistenceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Handle_WhenWalletDoesNotExist_ShouldPersistWallet()
    {
        await ExecuteInTransactionAsync(async serviceProvider =>
        {
            // Arrange
            var ownerId = Guid.NewGuid();
            var command = new CreateWalletCommand(ownerId, "try");
            var handler = serviceProvider.GetRequiredService<CreateWalletHandler>();
            var dbContext = serviceProvider.GetRequiredService<LedgerlyDbContext>();

            // Act
            var result = await handler.Handle(command);

            dbContext.ChangeTracker.Clear();

            var persistedWallet = await dbContext.Wallets
                .AsNoTracking()
                .SingleAsync(wallet => wallet.Id == result.WalletId);

            // Assert
            Assert.Equal(ownerId, persistedWallet.OwnerId);
            Assert.Equal("TRY", persistedWallet.Currency.Code);
            Assert.Equal(WalletStatus.Active, persistedWallet.Status);
            Assert.Equal(0m, persistedWallet.Balance);
            Assert.Equal(PostgresFixture.FixedUtcNow, persistedWallet.CreatedAtUtc);
        });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Handle_WhenWalletAlreadyExists_ShouldRejectDuplicate()
    {
        await ExecuteInTransactionAsync(async serviceProvider =>
        {
            // Arrange
            var ownerId = Guid.NewGuid();
            var command = new CreateWalletCommand(ownerId, "TRY");
            var handler = serviceProvider.GetRequiredService<CreateWalletHandler>();
            var dbContext = serviceProvider.GetRequiredService<LedgerlyDbContext>();

            await handler.Handle(command);

            // Act
            var exception = await Assert.ThrowsAsync<WalletAlreadyExistsException>(
                () => handler.Handle(command)
            );

            dbContext.ChangeTracker.Clear();

            var walletCount = await dbContext.Wallets
                .AsNoTracking()
                .CountAsync(wallet => wallet.OwnerId == ownerId);

            // Assert
            Assert.Equal(ownerId, exception.OwnerId);
            Assert.Equal("TRY", exception.CurrencyCode);
            Assert.Equal(1, walletCount);
        });
    }

    private async Task ExecuteInTransactionAsync(
        Func<IServiceProvider, Task> test
    )
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        try
        {
            await test(scope.ServiceProvider);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
