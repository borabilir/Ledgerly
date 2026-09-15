using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Application.Wallets.CreateWallet;
using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Persistence;
using Ledgerly.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ledgerly.IntegrationTests.Wallets;

[Collection(PostgresCollection.Name)]
public sealed class WalletPersistenceExceptionTests
{
    private readonly PostgresFixture _fixture;

    public WalletPersistenceExceptionTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SaveChanges_WhenOwnerCurrencyConflicts_ShouldTranslateAndPreserveCause()
    {
        await ExecuteInTransactionAsync(async dbContext =>
        {
            var ownerId = Guid.NewGuid();
            dbContext.Wallets.Add(CreateWallet(ownerId));
            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            dbContext.Wallets.Add(CreateWallet(ownerId));

            var exception = await Assert.ThrowsAsync<WalletAlreadyExistsException>(
                () => ((IUnitOfWork)dbContext).SaveChangesAsync()
            );

            Assert.Equal(ownerId, exception.OwnerId);
            Assert.Equal("TRY", exception.CurrencyCode);
            var updateException = Assert.IsType<DbUpdateException>(exception.InnerException);
            var postgresException = Assert.IsType<PostgresException>(updateException.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
            Assert.Equal("ux_wallets_owner_id_currency", postgresException.ConstraintName);
        });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SaveChanges_WhenPrimaryKeyConflicts_ShouldPreserveDatabaseError()
    {
        await ExecuteInTransactionAsync(async dbContext =>
        {
            var existing = CreateWallet(Guid.NewGuid());
            dbContext.Wallets.Add(existing);
            await dbContext.SaveChangesAsync();
            dbContext.ChangeTracker.Clear();

            // A different owner isolates the primary-key conflict from owner/currency uniqueness.
            var duplicateId = CreateWallet(Guid.NewGuid());
            dbContext.Entry(duplicateId).Property(wallet => wallet.Id).CurrentValue = existing.Id;
            dbContext.Wallets.Add(duplicateId);

            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => ((IUnitOfWork)dbContext).SaveChangesAsync()
            );

            var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
            Assert.Equal("pk_wallets", postgresException.ConstraintName);
        });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SaveChanges_WhenNumericValueOverflows_ShouldPreserveDatabaseError()
    {
        await ExecuteInTransactionAsync(async dbContext =>
        {
            var wallet = CreateWallet(Guid.NewGuid());
            dbContext.Wallets.Add(wallet);
            // Deliberately inject invalid persistence state without adding a domain mutation API.
            dbContext.Entry(wallet).Property(value => value.Balance).CurrentValue = decimal.MaxValue;

            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => ((IUnitOfWork)dbContext).SaveChangesAsync()
            );

            var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.NumericValueOutOfRange, postgresException.SqlState);
        });
    }

    [Theory]
    [InlineData("different-sql-state")]
    [InlineData("non-postgres")]
    [InlineData("no-inner-exception")]
    public async Task SaveChanges_WhenFailureIsUnrelated_ShouldRethrowSameException(string failure)
    {
        Exception? cause = failure switch
        {
            "different-sql-state" => new PostgresException(
                "Injected failure", "ERROR", "ERROR", PostgresErrorCodes.CheckViolation,
                constraintName: "ux_wallets_owner_id_currency"
            ),
            "non-postgres" => new InvalidOperationException("Injected provider failure"),
            _ => null,
        };
        var interceptor = new FailingSaveInterceptor(cause);
        var options = new DbContextOptionsBuilder<LedgerlyDbContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .AddInterceptors(interceptor)
            .Options;
        await using var dbContext = new LedgerlyDbContext(options);
        dbContext.Wallets.Add(CreateWallet(Guid.NewGuid()));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => ((IUnitOfWork)dbContext).SaveChangesAsync()
        );

        Assert.Same(interceptor.Failure, exception);
        Assert.Same(cause, exception.InnerException);
    }

    private static Wallet CreateWallet(Guid ownerId) =>
        Wallet.Create(ownerId, Currency.FromCode("TRY"), PostgresFixture.FixedUtcNow);

    private async Task ExecuteInTransactionAsync(Func<LedgerlyDbContext, Task> test)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerlyDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        try
        {
            await test(dbContext);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private sealed class FailingSaveInterceptor(Exception? cause) : SaveChangesInterceptor
    {
        public DbUpdateException? Failure { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Failure = new DbUpdateException(
                "Injected save failure", cause, eventData.Context!.ChangeTracker.Entries().ToArray()
            );
            throw Failure;
        }
    }
}
