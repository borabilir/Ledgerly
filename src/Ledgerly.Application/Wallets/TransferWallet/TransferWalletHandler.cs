using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Application.Ledger;
using Ledgerly.Domain.Ledger;
using Ledgerly.Domain.Wallets;

namespace Ledgerly.Application.Wallets.TransferWallet;

public sealed class TransferWalletHandler(
    IWalletRepository wallets,
    ILedgerAccountRepository accounts,
    IJournalEntryRepository journals,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<TransferWalletResult?> Handle(
        TransferWalletCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.SourceWalletId == command.DestinationWalletId)
        {
            throw new SameWalletTransferException();
        }

        var source = await wallets.GetForUpdateAsync(command.SourceWalletId, cancellationToken);
        var destination = await wallets.GetForUpdateAsync(command.DestinationWalletId, cancellationToken);
        if (source is null || destination is null)
        {
            return null;
        }

        if (source.Currency != destination.Currency)
        {
            throw new TransferCurrencyMismatchException(source.Currency.Code, destination.Currency.Code);
        }

        // Domain methods protect each wallet's balance invariant. Both tracked wallets,
        // the journal and any new accounts are committed by the single save below.
        source.Debit(command.Amount);
        destination.Credit(command.Amount);

        var now = timeProvider.GetUtcNow();
        var sourceAccountId = await GetOrCreateAccountId(source.Id, source.Currency, now, cancellationToken);
        var destinationAccountId = await GetOrCreateAccountId(
            destination.Id, destination.Currency, now, cancellationToken);
        var journal = JournalEntry.Create(source.Currency,
        [
            Posting.Create(sourceAccountId, PostingDirection.Debit, command.Amount),
            Posting.Create(destinationAccountId, PostingDirection.Credit, command.Amount),
        ], now);

        journals.Add(journal);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TransferWalletResult(
            journal.Id,
            source.Id,
            destination.Id,
            source.Currency.Code,
            command.Amount,
            source.Balance,
            destination.Balance);
    }

    private async Task<Guid> GetOrCreateAccountId(
        Guid walletId,
        Currency currency,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await accounts.GetByWalletIdAsync(walletId, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var account = LedgerAccount.CreateForWallet(walletId, currency, now);
        accounts.Add(account);
        return account.Id;
    }
}
