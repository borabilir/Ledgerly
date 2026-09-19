namespace Ledgerly.Application.Wallets.TransferWallet;

public sealed class SameWalletTransferException()
    : Exception("Source and destination wallets must be different.");

public sealed class TransferCurrencyMismatchException(string sourceCurrency, string destinationCurrency)
    : Exception($"Cannot transfer from '{sourceCurrency}' to '{destinationCurrency}' without currency conversion.");

public sealed class TransferIdempotencyConflictException()
    : Exception("This idempotency key was already used for a different transfer request.");

public sealed class TransferIdempotencyWriteConflictException(Exception innerException)
    : Exception("A transfer with the same idempotency key is being processed.", innerException);
