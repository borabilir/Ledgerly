namespace Ledgerly.Application.Wallets.TransferWallet;

public sealed class SameWalletTransferException()
    : Exception("Source and destination wallets must be different.");

public sealed class TransferCurrencyMismatchException(string sourceCurrency, string destinationCurrency)
    : Exception($"Cannot transfer from '{sourceCurrency}' to '{destinationCurrency}' without currency conversion.");
