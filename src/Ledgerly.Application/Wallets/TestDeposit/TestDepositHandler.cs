using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Application.Ledger;
using Ledgerly.Domain.Ledger;

namespace Ledgerly.Application.Wallets.TestDeposit;

public sealed class TestDepositHandler(
    IWalletRepository wallets,
    ILedgerAccountRepository accounts,
    IJournalEntryRepository journals,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<TestDepositResult?> Handle(TestDepositCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
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
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new TestDepositResult(wallet.Id, journal.Id, wallet.Currency.Code, command.Amount, wallet.Balance);
    }
}
