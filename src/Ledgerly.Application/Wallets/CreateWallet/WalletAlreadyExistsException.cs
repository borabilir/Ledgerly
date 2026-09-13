namespace Ledgerly.Application.Wallets.CreateWallet;

public sealed class WalletAlreadyExistsException : Exception
{
    public WalletAlreadyExistsException(Guid ownerId, string currencyCode)
        : base($"Owner '{ownerId}' already has a '{currencyCode}' wallet.")
    {
        OwnerId = ownerId;
        CurrencyCode = currencyCode;
    }

    public Guid OwnerId { get; }

    public string CurrencyCode { get; }
}
