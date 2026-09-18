using Ledgerly.Application.Ledger;
using Ledgerly.Domain.Ledger;
using Ledgerly.Domain.Wallets;
using Ledgerly.Infrastructure.Persistence;
using Ledgerly.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace Ledgerly.Infrastructure.Ledger;

internal sealed class LedgerAccountRepository(LedgerlyDbContext dbContext) : ILedgerAccountRepository
{
    public void Add(LedgerAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        dbContext.LedgerAccounts.Add(new LedgerAccountRecord
        {
            Id = account.Id,
            WalletId = account.WalletId,
            Type = account.Type,
            Purpose = account.Purpose,
            Currency = account.Currency,
            CreatedAtUtc = account.CreatedAtUtc,
        });
    }

    public Task<LedgerAccountSnapshot?> GetByWalletIdAsync(Guid walletId, CancellationToken cancellationToken = default) =>
        ReadAccounts(dbContext.LedgerAccounts.Where(account => account.WalletId == walletId))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<LedgerAccountSnapshot?> GetTestFundingAsync(Currency currency, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return ReadAccounts(dbContext.LedgerAccounts.Where(
            account => account.Purpose == LedgerAccountPurpose.TestFunding
                && account.Currency == currency)).SingleOrDefaultAsync(cancellationToken);
    }

    private static IQueryable<LedgerAccountSnapshot> ReadAccounts(IQueryable<LedgerAccountRecord> accounts) =>
        accounts.AsNoTracking().Select(account => new LedgerAccountSnapshot(
            account.Id, account.WalletId, account.Type, account.Purpose, account.Currency.Code, account.CreatedAtUtc));
}
