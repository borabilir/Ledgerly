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
        dbContext.Set<LedgerAccountRecord>().Add(new LedgerAccountRecord
        {
            Id = account.Id,
            WalletId = account.WalletId,
            Type = account.Type,
            Currency = account.Currency,
            CreatedAtUtc = account.CreatedAtUtc,
        });
    }

    public Task<LedgerAccountSnapshot?> GetByWalletIdAsync(Guid walletId, CancellationToken cancellationToken = default) =>
        ReadAccounts(dbContext.Set<LedgerAccountRecord>().Where(account => account.WalletId == walletId))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<LedgerAccountSnapshot?> GetTestFundingAsync(Currency currency, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return ReadAccounts(dbContext.Set<LedgerAccountRecord>().Where(
            account => account.WalletId == null && account.Type == LedgerAccountType.Asset
                && account.Currency == currency)).SingleOrDefaultAsync(cancellationToken);
    }

    private static IQueryable<LedgerAccountSnapshot> ReadAccounts(IQueryable<LedgerAccountRecord> accounts) =>
        accounts.AsNoTracking().Select(account => new LedgerAccountSnapshot(
            account.Id, account.WalletId, account.Type, account.Currency.Code, account.CreatedAtUtc));
}
