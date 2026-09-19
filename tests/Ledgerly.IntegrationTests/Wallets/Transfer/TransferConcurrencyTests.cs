using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Application.Ledger;
using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Persistence;
using Ledgerly.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Ledgerly.IntegrationTests.Wallets.Transfer;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Lab", "WalletTransfer")]
public sealed class TransferConcurrencyTests(PostgresFixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoTransfersSpendingSameBalance_ShouldShowWhyTheOriginalBalanceMustBeGuarded(bool useGuard)
    {
        var source = NewWallet();
        source.Credit(100m);
        var firstDestination = NewWallet();
        var secondDestination = NewWallet();
        var ids = new[] { source.Id, firstDestination.Id, secondDestination.Id };
        await using var seedScope = fixture.Services.CreateAsyncScope();
        var seed = seedScope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
        seed.Wallets.AddRange(source, firstDestination, secondDestination);
        await seed.SaveChangesAsync();
        try
        {
            await using var firstScope = fixture.Services.CreateAsyncScope();
            await using var secondScope = fixture.Services.CreateAsyncScope();
            var first = firstScope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
            var second = secondScope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
            var firstSource = await first.Wallets.SingleAsync(wallet => wallet.Id == source.Id);
            var secondSource = await second.Wallets.SingleAsync(wallet => wallet.Id == source.Id);
            var firstTarget = await first.Wallets.SingleAsync(wallet => wallet.Id == firstDestination.Id);
            var secondTarget = await second.Wallets.SingleAsync(wallet => wallet.Id == secondDestination.Id);
            Assert.Equal(100m, firstSource.Balance);
            Assert.Equal(100m, secondSource.Balance);
            firstSource.Debit(80m);
            firstTarget.Credit(80m);
            secondSource.Debit(80m);
            secondTarget.Credit(80m);

            if (useGuard)
            {
                await first.SaveChangesAsync();
                await Assert.ThrowsAsync<LedgerWriteConflictException>(() =>
                    ((IUnitOfWork)second).SaveChangesAsync());
            }
            else
            {
                // Deliberately broken baseline: both decisions were made from the same 100 TRY,
                // and these direct updates bypass EF's original-balance concurrency condition.
                await first.Wallets.Where(wallet => wallet.Id == source.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(wallet => wallet.Balance, firstSource.Balance));
                await first.Wallets.Where(wallet => wallet.Id == firstDestination.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(wallet => wallet.Balance, firstTarget.Balance));
                await second.Wallets.Where(wallet => wallet.Id == source.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(wallet => wallet.Balance, secondSource.Balance));
                await second.Wallets.Where(wallet => wallet.Id == secondDestination.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(wallet => wallet.Balance, secondTarget.Balance));
            }

            await using var verifyScope = fixture.Services.CreateAsyncScope();
            var verify = verifyScope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
            var balances = await verify.Wallets.Where(wallet => ids.Contains(wallet.Id))
                .ToDictionaryAsync(wallet => wallet.Id, wallet => wallet.Balance);
            var total = balances.Values.Sum();
            output.WriteLine($"Guard: {useGuard}; source: {balances[source.Id]}; " +
                $"destinations: {balances[firstDestination.Id]} + {balances[secondDestination.Id]}; total: {total}");
            Assert.Equal(20m, balances[source.Id]);
            Assert.Equal(useGuard ? 80m : 160m,
                balances[firstDestination.Id] + balances[secondDestination.Id]);
            Assert.Equal(useGuard ? 100m : 180m, total);
        }
        finally
        {
            await seed.Wallets.Where(wallet => ids.Contains(wallet.Id)).ExecuteDeleteAsync();
        }
    }

    private static Wallet NewWallet() =>
        Wallet.Create(Guid.NewGuid(), Currency.FromCode("TRY"), PostgresFixture.FixedUtcNow);
}
