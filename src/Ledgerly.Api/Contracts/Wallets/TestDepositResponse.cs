namespace Ledgerly.Api.Contracts.Wallets;

public sealed record TestDepositResponse(Guid WalletId, Guid JournalEntryId, string CurrencyCode, decimal Amount, decimal Balance);
