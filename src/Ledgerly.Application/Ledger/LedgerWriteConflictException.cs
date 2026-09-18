namespace Ledgerly.Application.Ledger;

public sealed class LedgerWriteConflictException(Exception innerException)
    : Exception("The wallet balance or ledger accounts changed while this operation was being saved. No changes from this operation were committed.", innerException);
