using Ledgerly.Application.Wallets.TestDeposit;
using Ledgerly.Infrastructure.Persistence;
using Ledgerly.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace Ledgerly.Infrastructure.Wallets;

internal sealed class TestDepositOperationRepository(LedgerlyDbContext dbContext) : ITestDepositOperationRepository
{
    public async Task<TestDepositOperationSnapshot?> GetAsync(Guid walletId, string key,
        CancellationToken cancellationToken = default)
    {
        var operation = await dbContext.TestDepositOperations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.WalletId == walletId && row.Key == key, cancellationToken);
        return operation is null ? null : new TestDepositOperationSnapshot(operation.Amount,
            new TestDepositResult(operation.WalletId, operation.JournalEntryId, operation.CurrencyCode,
                operation.Amount, operation.Balance));
    }

    public void Add(Guid walletId, string key, decimal amount, TestDepositResult result) =>
        dbContext.TestDepositOperations.Add(new TestDepositOperationRecord
        {
            WalletId = walletId,
            Key = key,
            Amount = amount,
            JournalEntryId = result.JournalEntryId,
            CurrencyCode = result.CurrencyCode,
            Balance = result.Balance,
        });
}
