using Ledgerly.Application.Wallets;
using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Persistence;
using Ledgerly.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ledgerly.IntegrationTests.Wallets;

[Collection(PostgresCollection.Name)]
public sealed class GetWalletPersistenceTests
{
    private readonly PostgresFixture _fixture;

    public GetWalletPersistenceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetById_WhenWalletExists_ShouldReadWithoutTrackingChanges()
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IWalletRepository>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        try
        {
            var wallet = Wallet.Create(
                Guid.NewGuid(), Currency.FromCode("TRY"), PostgresFixture.FixedUtcNow
            );
            dbContext.Wallets.Add(wallet);
            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            var snapshot = await repository.GetByIdAsync(wallet.Id);

            Assert.NotNull(snapshot);
            Assert.Equal(wallet.Id, snapshot.Id);
            Assert.Equal(wallet.OwnerId, snapshot.OwnerId);
            Assert.Equal(wallet.Currency, snapshot.Currency);
            Assert.Equal(wallet.CreatedAtUtc, snapshot.CreatedAtUtc);
            Assert.Empty(dbContext.ChangeTracker.Entries());

            // Editing a read snapshot must not silently turn a later save into a write.
            dbContext.Entry(snapshot).Property(value => value.Balance).CurrentValue = 100m;
            Assert.Equal(0, await dbContext.SaveChangesAsync());
            var persistedBalance = await dbContext.Wallets
                .Where(value => value.Id == wallet.Id)
                .Select(value => value.Balance)
                .SingleAsync();
            Assert.Equal(0m, persistedBalance);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
