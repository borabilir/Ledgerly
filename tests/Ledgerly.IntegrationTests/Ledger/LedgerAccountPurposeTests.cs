using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Domain.Ledger;
using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Ledger;
using Ledgerly.Infrastructure.Persistence;
using Ledgerly.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ledgerly.IntegrationTests.Ledger;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class LedgerAccountPurposeTests(PostgresFixture fixture)
{
    [Fact]
    public async Task GetTestFunding_WhenOnlyWalletAccountExists_ShouldReturnNullThenReadExplicitFundingPurpose()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var currency = Currency.FromCode("TRY");
            var wallet = Wallet.Create(Guid.NewGuid(), currency, PostgresFixture.FixedUtcNow);
            var repository = new LedgerAccountRepository(db);
            db.Wallets.Add(wallet);
            repository.Add(LedgerAccount.CreateForWallet(wallet.Id, currency, PostgresFixture.FixedUtcNow));
            await ((IUnitOfWork)db).SaveChangesAsync();
            db.ChangeTracker.Clear();
            Assert.Null(await repository.GetTestFundingAsync(currency));

            var funding = LedgerAccount.CreateTestFunding(currency, PostgresFixture.FixedUtcNow);
            repository.Add(funding);
            await ((IUnitOfWork)db).SaveChangesAsync();
            db.ChangeTracker.Clear();
            var result = await repository.GetTestFundingAsync(currency);
            Assert.NotNull(result);
            Assert.Equal(funding.Id, result.Id);
            Assert.Equal(LedgerAccountPurpose.TestFunding, result.Purpose);
            Assert.Equal(LedgerAccountPurpose.Wallet, (await repository.GetByWalletIdAsync(wallet.Id))!.Purpose);
            Assert.Empty(db.ChangeTracker.Entries());
        }
        finally { await transaction.RollbackAsync(); }
    }

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(3, 1, false)]
    [InlineData(1, 1, false)]
    [InlineData(1, 2, false)]
    [InlineData(2, 2, true)]
    [InlineData(2, 1, true)]
    public async Task Database_WhenPurposeDoesNotMatchAccount_ShouldRejectIt(int purpose, int type, bool linked)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var now = PostgresFixture.FixedUtcNow;
            var wallet = Wallet.Create(Guid.NewGuid(), Currency.FromCode("TRY"), now);
            db.Wallets.Add(wallet);
            await db.SaveChangesAsync();
            var id = Guid.NewGuid();
            Guid? walletId = linked ? wallet.Id : null;
            var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO ledger_accounts (id, wallet_id, type, purpose, currency, created_at_utc)
                VALUES ({id}, {walletId}, {type}, {purpose}, 'TRY', {now})
                """));
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
            Assert.Equal("ck_ledger_accounts_purpose_type_wallet", exception.ConstraintName);
        }
        finally { await transaction.RollbackAsync(); }
    }

    [Fact]
    public async Task Database_WhenPurposeIsOmitted_ShouldRejectInsteadOfDefaultingToTestFunding()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var id = Guid.NewGuid();
            var now = PostgresFixture.FixedUtcNow;
            var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO ledger_accounts (id, wallet_id, type, currency, created_at_utc)
                VALUES ({id}, NULL, 1, 'TRY', {now})
                """));
            Assert.Equal(PostgresErrorCodes.NotNullViolation, exception.SqlState);
            Assert.Equal("purpose", exception.ColumnName);
        }
        finally { await transaction.RollbackAsync(); }
    }
}
