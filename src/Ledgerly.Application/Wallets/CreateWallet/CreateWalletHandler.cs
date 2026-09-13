using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Domain.Wallets;

namespace Ledgerly.Application.Wallets.CreateWallet;

public sealed class CreateWalletHandler
{
    private readonly IWalletRepository _walletRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public CreateWalletHandler(
        IWalletRepository walletRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider
    )
    {
        ArgumentNullException.ThrowIfNull(walletRepository);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _walletRepository = walletRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<CreateWalletResult> Handle(
        CreateWalletCommand command,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(command);

        var currency = Currency.FromCode(command.CurrencyCode);

        var walletExists = await _walletRepository.ExistsAsync(
            command.OwnerId,
            currency,
            cancellationToken
        );

        if (walletExists)
        {
            throw new WalletAlreadyExistsException(command.OwnerId, currency.Code);
        }

        var wallet = Wallet.Create(
            command.OwnerId,
            currency,
            _timeProvider.GetUtcNow()
        );

        _walletRepository.Add(wallet);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateWalletResult(wallet.Id);
    }
}
