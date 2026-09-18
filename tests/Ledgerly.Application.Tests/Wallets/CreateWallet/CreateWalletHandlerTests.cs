using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Application.Wallets;
using Ledgerly.Application.Wallets.CreateWallet;
using Ledgerly.Domain.Wallets;

namespace Ledgerly.Application.Tests.Wallets.CreateWallet;

public sealed class CreateWalletHandlerTests
{
    [Fact]
    public async Task Handle_WhenWalletDoesNotExist_ShouldCreateAndPersistWallet()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        var command = new CreateWalletCommand(ownerId, "TRY");

        var walletRepository = new FakeWalletRepository(walletExists: false);
        var unitOfWork = new FakeUnitOfWork();
        var timeProvider = new StubTimeProvider(now);

        var handler = new CreateWalletHandler(
            walletRepository,
            unitOfWork,
            timeProvider
        );

        // Act
        var result = await handler.Handle(command);

        // Assert
        var addedWallet = Assert.IsType<Wallet>(walletRepository.AddedWallet);

        Assert.Equal(result.WalletId, addedWallet.Id);
        Assert.Equal(ownerId, addedWallet.OwnerId);
        Assert.Equal("TRY", addedWallet.Currency.Code);
        Assert.Equal(WalletStatus.Active, addedWallet.Status);
        Assert.Equal(0m, addedWallet.Balance);
        Assert.Equal(now, addedWallet.CreatedAtUtc);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_WhenWalletAlreadyExists_ShouldThrowAndNotPersistWallet()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
        var command = new CreateWalletCommand(ownerId, "try");

        var walletRepository = new FakeWalletRepository(walletExists: true);
        var unitOfWork = new FakeUnitOfWork();
        var timeProvider = new StubTimeProvider(now);

        var handler = new CreateWalletHandler(
            walletRepository,
            unitOfWork,
            timeProvider
        );

        // Act
        var exception = await Assert.ThrowsAsync<WalletAlreadyExistsException>(
            () => handler.Handle(command)
        );

        // Assert
        Assert.Equal(ownerId, exception.OwnerId);
        Assert.Equal("TRY", exception.CurrencyCode);
        Assert.Null(walletRepository.AddedWallet);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    private sealed class FakeWalletRepository : IWalletRepository
    {
        private readonly bool _walletExists;

        public FakeWalletRepository(bool walletExists)
        {
            _walletExists = walletExists;
        }

        public Wallet? AddedWallet { get; private set; }

        public Task<Wallet?> GetByIdAsync(Guid walletId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Create Wallet must not read by ID.");
        }

        public Task<Wallet?> GetForUpdateAsync(Guid walletId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This handler must not load a wallet for update.");

        public Task<bool> ExistsAsync(
            Guid ownerId,
            Currency currency,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(_walletExists);
        }

        public void Add(Wallet wallet)
        {
            AddedWallet = wallet;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCallCount { get; private set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCallCount++;

            return Task.CompletedTask;
        }
    }

    private sealed class StubTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public StubTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
