namespace Ledgerly.Application.Wallets.TestDeposit;

public sealed record TestDepositResult(Guid WalletId, Guid JournalEntryId, string CurrencyCode, decimal Amount, decimal Balance);
