namespace Ledgerly.Domain.Wallets;

public sealed class InsufficientFundsException(decimal availableBalance, decimal requestedAmount)
    : Exception($"The wallet balance '{availableBalance}' is insufficient for amount '{requestedAmount}'.")
{
    public decimal AvailableBalance { get; } = availableBalance;
    public decimal RequestedAmount { get; } = requestedAmount;
}
