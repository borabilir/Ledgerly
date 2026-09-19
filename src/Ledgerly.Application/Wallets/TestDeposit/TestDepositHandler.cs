using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Application.Ledger;
using Ledgerly.Domain.Ledger;

namespace Ledgerly.Application.Wallets.TestDeposit;

public sealed class TestDepositHandler(
    IWalletRepository wallets,
    ILedgerAccountRepository accounts,
    IJournalEntryRepository journals,
    ITestDepositOperationRepository operations,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<TestDepositResult?> Handle(TestDepositCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey)
            || command.IdempotencyKey.Length > 128
            || command.IdempotencyKey != command.IdempotencyKey.Trim())
        {
            throw new ArgumentException("Idempotency key must be 1–128 characters without surrounding whitespace.", nameof(command));
        }

        var previous = await operations.GetAsync(command.WalletId, command.IdempotencyKey, cancellationToken);
        if (previous is not null)
        {
            return Replay(previous, command.Amount);
        }

        var wallet = await wallets.GetForUpdateAsync(command.WalletId, cancellationToken);
        if (wallet is null)
        {
            return null;
        }

        // Domain validation runs before staging any accounts or journal rows.
        wallet.Credit(command.Amount);
        var now = timeProvider.GetUtcNow();
        var customer = await accounts.GetByWalletIdAsync(wallet.Id, cancellationToken);
        var funding = await accounts.GetTestFundingAsync(wallet.Currency, cancellationToken);
        Guid customerId;
        Guid fundingId;
        if (customer is null)
        {
            var account = LedgerAccount.CreateForWallet(wallet.Id, wallet.Currency, now);
            accounts.Add(account);
            customerId = account.Id;
        }
        else
        {
            customerId = customer.Id;
        }

        if (funding is null)
        {
            var account = LedgerAccount.CreateTestFunding(wallet.Currency, now);
            accounts.Add(account);
            fundingId = account.Id;
        }
        else
        {
            fundingId = funding.Id;
        }

        var journal = JournalEntry.Create(wallet.Currency,
            [Posting.Create(fundingId, PostingDirection.Debit, command.Amount),
             Posting.Create(customerId, PostingDirection.Credit, command.Amount)], now);
        journals.Add(journal);
        var result = new TestDepositResult(wallet.Id, journal.Id, wallet.Currency.Code, command.Amount, wallet.Balance);
        operations.Add(wallet.Id, command.IdempotencyKey, command.Amount, result);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is LedgerWriteConflictException or TestDepositIdempotencyWriteConflictException)
        {
            // A concurrent request may have committed the same key. Read its committed receipt;
            // do not treat a conflict from an unrelated deposit as a successful replay.
            previous = await operations.GetAsync(command.WalletId, command.IdempotencyKey, cancellationToken);
            if (previous is null)
            {
                throw;
            }

            return Replay(previous, command.Amount);
        }

        return result;
    }

    private static TestDepositResult Replay(TestDepositOperationSnapshot previous, decimal amount)
    {
        if (previous.Amount != amount)
        {
            throw new TestDepositIdempotencyConflictException();
        }

        return previous.Result;
    }
}
