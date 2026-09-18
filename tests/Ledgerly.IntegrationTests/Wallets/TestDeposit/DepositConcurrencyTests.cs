using Ledgerly.Domain.Wallets;
using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Application.Ledger;
using Ledgerly.Infrastructure.Persistence;
using Ledgerly.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Ledgerly.IntegrationTests.Wallets.TestDeposit;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Lab", "TestDeposit")]
public sealed class DepositConcurrencyTests(PostgresFixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoStaleBalances_ShouldDemonstrateWhyOriginalBalanceMatters(bool useGuard)
    {
        await using var seedScope = fixture.Services.CreateAsyncScope();
        var seed = seedScope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
        var wallet = Wallet.Create(Guid.NewGuid(), Currency.FromCode("TRY"), PostgresFixture.FixedUtcNow);
        seed.Wallets.Add(wallet);
        await seed.SaveChangesAsync();
        try
        {
            await using var firstScope = fixture.Services.CreateAsyncScope();
            await using var secondScope = fixture.Services.CreateAsyncScope();
            var first = firstScope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
            var second = secondScope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
            var a = await first.Wallets.SingleAsync(value => value.Id == wallet.Id);
            var b = await second.Wallets.SingleAsync(value => value.Id == wallet.Id);
            Assert.Equal(0m, a.Balance);
            Assert.Equal(0m, b.Balance);
            a.Credit(10m);
            b.Credit(20m);
            if (useGuard)
            {
                await first.SaveChangesAsync();
                var conflict = await Assert.ThrowsAsync<LedgerWriteConflictException>(() => ((IUnitOfWork)second).SaveChangesAsync());
                Assert.IsType<DbUpdateConcurrencyException>(conflict.InnerException);
            }
            else
            {
                // Deliberately unsafe lab writes: ExecuteUpdate does not apply tracked concurrency tokens.
                // The amounts were both calculated from the stale value 0 before either write.
                await first.Wallets.Where(w => w.Id == wallet.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(w => w.Balance, a.Balance));
                await second.Wallets.Where(w => w.Id == wallet.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(w => w.Balance, b.Balance));
            }
            var balance = await seed.Wallets.Where(value => value.Id == wallet.Id).Select(value => value.Balance).SingleAsync();
            output.WriteLine($"Guard: {useGuard}; both requests read 0; deposits 10 + 20; persisted balance: {balance}");
            Assert.Equal(useGuard ? 10m : 20m, balance);
        }
        finally
        {
            await seed.Wallets.Where(value => value.Id == wallet.Id).ExecuteDeleteAsync();
        }
    }
}
