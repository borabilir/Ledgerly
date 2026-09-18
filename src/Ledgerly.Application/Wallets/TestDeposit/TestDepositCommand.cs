namespace Ledgerly.Application.Wallets.TestDeposit;

public sealed record TestDepositCommand(Guid WalletId, decimal Amount);
