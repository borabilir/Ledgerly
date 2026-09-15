namespace Ledgerly.Application.Wallets.GetWallet;

public sealed class GetWalletHandler
{
    private readonly IWalletRepository _walletRepository;

    public GetWalletHandler(IWalletRepository walletRepository)
    {
        ArgumentNullException.ThrowIfNull(walletRepository);
        _walletRepository = walletRepository;
    }

    public async Task<GetWalletResult?> Handle(
        GetWalletQuery query,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(query);

        var wallet = await _walletRepository.GetByIdAsync(query.WalletId, cancellationToken);

        return wallet is null
            ? null
            : new GetWalletResult(
                wallet.Id,
                wallet.OwnerId,
                wallet.Currency.Code,
                wallet.Status,
                wallet.Balance,
                wallet.CreatedAtUtc
            );
    }
}
