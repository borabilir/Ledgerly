using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Application.Ledger;
using Ledgerly.Domain.Ledger;
using Ledgerly.Domain.Wallets;

namespace Ledgerly.Application.Wallets.TransferWallet;

public sealed class TransferWalletHandler(
    IWalletRepository wallets,
    ILedgerAccountRepository accounts,
    IJournalEntryRepository journals,
    IWalletTransferRepository transfers,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<TransferWalletResult?> Handle(
        TransferWalletCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey)
            || command.IdempotencyKey.Length > 128
            || command.IdempotencyKey != command.IdempotencyKey.Trim())
        {
            throw new ArgumentException(
                "Idempotency key must be 1-128 characters without surrounding whitespace.",
                nameof(command));
        }

        var previous = await transfers.GetAsync(
            command.SourceWalletId, command.IdempotencyKey, cancellationToken);
        if (previous is not null)
        {
            return Replay(previous, command);
        }

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
        var result = new TransferWalletResult(
            Guid.NewGuid(),
            journal.Id,
            source.Id,
            destination.Id,
            source.Currency.Code,
            command.Amount,
            source.Balance,
            destination.Balance);
        transfers.Add(command.IdempotencyKey, result, now);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (
            exception is LedgerWriteConflictException or TransferIdempotencyWriteConflictException)
        {
            // The competing request may already have committed this key. If it did,
            // return that durable receipt; otherwise preserve the original conflict.
            previous = await transfers.GetAsync(
                command.SourceWalletId, command.IdempotencyKey, cancellationToken);
            if (previous is null)
            {
                throw;
            }

            return Replay(previous, command);
        }

        return result;
    }

    private static TransferWalletResult Replay(
        WalletTransferSnapshot previous,
        TransferWalletCommand command)
    {
        if (previous.Result.DestinationWalletId != command.DestinationWalletId
            || previous.Result.Amount != command.Amount)
        {
            throw new TransferIdempotencyConflictException();
        }

        return previous.Result;
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
