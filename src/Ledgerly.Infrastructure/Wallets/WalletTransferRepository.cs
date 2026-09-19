using Ledgerly.Application.Wallets.TransferWallet;
using Ledgerly.Infrastructure.Persistence;
using Ledgerly.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace Ledgerly.Infrastructure.Wallets;

internal sealed class WalletTransferRepository(LedgerlyDbContext dbContext) : IWalletTransferRepository
{
    public async Task<WalletTransferSnapshot?> GetAsync(
        Guid sourceWalletId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var transfer = await dbContext.WalletTransfers.AsNoTracking().SingleOrDefaultAsync(
            row => row.SourceWalletId == sourceWalletId && row.IdempotencyKey == idempotencyKey,
            cancellationToken);

        return transfer is null
            ? null
            : new WalletTransferSnapshot(new TransferWalletResult(
                transfer.Id,
                transfer.JournalEntryId,
                transfer.SourceWalletId,
                transfer.DestinationWalletId,
                transfer.CurrencyCode,
                transfer.Amount,
                transfer.SourceBalance,
                transfer.DestinationBalance));
    }

    public void Add(string idempotencyKey, TransferWalletResult result, DateTimeOffset createdAtUtc) =>
        dbContext.WalletTransfers.Add(new WalletTransferRecord
        {
            Id = result.TransferId,
            SourceWalletId = result.SourceWalletId,
            DestinationWalletId = result.DestinationWalletId,
            IdempotencyKey = idempotencyKey,
            Amount = result.Amount,
            CurrencyCode = result.CurrencyCode,
            JournalEntryId = result.JournalEntryId,
            SourceBalance = result.SourceBalance,
            DestinationBalance = result.DestinationBalance,
            CreatedAtUtc = createdAtUtc,
        });
}
