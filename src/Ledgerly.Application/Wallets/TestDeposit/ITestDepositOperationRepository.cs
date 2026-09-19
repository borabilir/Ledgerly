namespace Ledgerly.Application.Wallets.TestDeposit;

public interface ITestDepositOperationRepository
{
    Task<TestDepositOperationSnapshot?> GetAsync(Guid walletId, string key, CancellationToken cancellationToken = default);
    void Add(Guid walletId, string key, decimal amount, TestDepositResult result);
}

public sealed record TestDepositOperationSnapshot(decimal Amount, TestDepositResult Result);
