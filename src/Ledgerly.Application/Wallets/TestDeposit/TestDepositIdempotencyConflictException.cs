namespace Ledgerly.Application.Wallets.TestDeposit;

public sealed class TestDepositIdempotencyConflictException()
    : Exception("This idempotency key was already used for a different deposit amount.");

public sealed class TestDepositIdempotencyWriteConflictException(Exception innerException)
    : Exception("Another request is using this idempotency key.", innerException);
