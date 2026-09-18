using Ledgerly.Application.Wallets;
using Ledgerly.Application.Wallets.GetWallet;
using Ledgerly.Domain.Wallets;

namespace Ledgerly.Application.Tests.Wallets.GetWallet;

public sealed class GetWalletHandlerTests
{
    [Fact]
    public async Task Handle_WhenWalletExists_ShouldReturnWalletDetails()
    {
        var wallet = Wallet.Create(
            Guid.NewGuid(),
            Currency.FromCode("TRY"),
            new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero)
        );
        var repository = new FakeWalletRepository(wallet);
        var handler = new GetWalletHandler(repository);
        using var cancellation = new CancellationTokenSource();

        var result = await handler.Handle(new GetWalletQuery(wallet.Id), cancellation.Token);

        Assert.NotNull(result);
        Assert.Equal(wallet.Id, result.WalletId);
        Assert.Equal(wallet.OwnerId, result.OwnerId);
        Assert.Equal("TRY", result.CurrencyCode);
        Assert.Equal(WalletStatus.Active, result.Status);
        Assert.Equal(0m, result.Balance);
        Assert.Equal(wallet.CreatedAtUtc, result.CreatedAtUtc);
        Assert.Equal(wallet.Id, repository.RequestedId);
        Assert.Equal(cancellation.Token, repository.CancellationToken);
    }

    [Fact]
    public async Task Handle_WhenWalletDoesNotExist_ShouldReturnNull()
    {
        var repository = new FakeWalletRepository(null);
        var handler = new GetWalletHandler(repository);
        var walletId = Guid.NewGuid();

        var result = await handler.Handle(new GetWalletQuery(walletId));

        Assert.Null(result);
        Assert.Equal(walletId, repository.RequestedId);
    }

    private sealed class FakeWalletRepository(Wallet? wallet) : IWalletRepository
    {
        public Guid? RequestedId { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<Wallet?> GetByIdAsync(Guid walletId, CancellationToken cancellationToken = default)
        {
            RequestedId = walletId;
            CancellationToken = cancellationToken;
            return Task.FromResult(wallet?.Id == walletId ? wallet : null);
        }

        public Task<Wallet?> GetForUpdateAsync(Guid walletId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This handler must not load a wallet for update.");

        public Task<bool> ExistsAsync(
            Guid ownerId,
            Currency currency,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException("Get Wallet must only read by ID.");

        public void Add(Wallet value) =>
            throw new NotSupportedException("Get Wallet must not add a wallet.");
    }
}
