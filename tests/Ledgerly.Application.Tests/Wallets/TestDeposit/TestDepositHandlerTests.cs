using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Application.Ledger;
using Ledgerly.Application.Wallets;
using Ledgerly.Application.Wallets.TestDeposit;
using Ledgerly.Domain.Ledger;
using Ledgerly.Domain.Wallets;

namespace Ledgerly.Application.Tests.Wallets.TestDeposit;

public sealed class TestDepositHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_ShouldCreateBalancedJournalAndSaveOnce(bool accountsExist)
    {
        var wallet = NewWallet();
        var accounts = new Accounts();
        if (accountsExist)
        {
            accounts.Customer = Snapshot(LedgerAccount.CreateForWallet(wallet.Id, wallet.Currency, Now));
            accounts.Funding = Snapshot(LedgerAccount.CreateTestFunding(wallet.Currency, Now));
        }
        var journals = new Journals();
        var saves = new Saves();
        var operations = new Operations();
        var handler = new TestDepositHandler(new Wallets(wallet), accounts, journals, operations, saves, new Clock());
        using var cancellation = new CancellationTokenSource();
        var result = await handler.Handle(new TestDepositCommand(wallet.Id, 12.3456m, "deposit-1"), cancellation.Token);

        Assert.NotNull(result);
        Assert.Equal(12.3456m, wallet.Balance);
        Assert.Equal(wallet.Id, result.WalletId);
        Assert.Equal(wallet.Balance, result.Balance);
        Assert.Equal("TRY", result.CurrencyCode);
        Assert.Equal(1, saves.Count);
        Assert.Equal(result, (await operations.GetAsync(wallet.Id, "deposit-1"))!.Result);
        Assert.Equal(cancellation.Token, saves.Token);
        Assert.Equal(accountsExist ? 0 : 2, accounts.Added.Count);
        var customer = accounts.Customer ?? Snapshot(Assert.Single(accounts.Added, a => a.WalletId == wallet.Id));
        var funding = accounts.Funding ?? Snapshot(Assert.Single(accounts.Added, a => a.WalletId == null));
        var journal = Assert.Single(journals.Added);
        Assert.Equal(journal.Id, result.JournalEntryId);
        Assert.Equal(Now, journal.CreatedAtUtc);
        Assert.Equal(Posting.Create(funding.Id, PostingDirection.Debit, result.Amount), journal.Postings[0]);
        Assert.Equal(Posting.Create(customer.Id, PostingDirection.Credit, result.Amount), journal.Postings[1]);
    }

    [Fact]
    public async Task Handle_WhenWalletIsMissing_ShouldNotStageOrSave()
    {
        var accounts = new Accounts();
        var journals = new Journals();
        var saves = new Saves();
        var handler = new TestDepositHandler(new Wallets(null), accounts, journals, new Operations(), saves, new Clock());
        Assert.Null(await handler.Handle(new TestDepositCommand(Guid.NewGuid(), 10m, "missing")));
        Assert.Equal(0, accounts.Reads);
        Assert.Empty(journals.Added);
        Assert.Equal(0, saves.Count);
    }

    [Fact]
    public async Task Handle_WhenAmountIsInvalid_ShouldNotStageOrSave()
    {
        var wallet = NewWallet();
        var accounts = new Accounts();
        var journals = new Journals();
        var saves = new Saves();
        var handler = new TestDepositHandler(new Wallets(wallet), accounts, journals, new Operations(), saves, new Clock());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => handler.Handle(new TestDepositCommand(wallet.Id, -1m, "invalid")));
        Assert.Equal(0m, wallet.Balance);
        Assert.Equal(0, accounts.Reads);
        Assert.Empty(journals.Added);
        Assert.Equal(0, saves.Count);
    }

    [Fact]
    public async Task Handle_WhenSaveConflicts_ShouldPropagateWithoutRetry()
    {
        var wallet = NewWallet();
        var error = new LedgerWriteConflictException(new InvalidOperationException("Test conflict"));
        var saves = new Saves { Failure = error };
        var handler = new TestDepositHandler(new Wallets(wallet), new Accounts(), new Journals(),
            new Operations { ReadAdded = false }, saves, new Clock());
        var actual = await Assert.ThrowsAsync<LedgerWriteConflictException>(() => handler.Handle(new TestDepositCommand(wallet.Id, 10m, "conflict")));
        Assert.Same(error, actual);
        Assert.Equal(1, saves.Count);
        // A fake does not prove rollback; the PostgreSQL HTTP tests do.
    }

    [Fact]
    public async Task Handle_WhenOperationAlreadyExists_ShouldReturnStoredReceiptWithoutSaving()
    {
        var walletId = Guid.NewGuid();
        var receipt = new TestDepositResult(walletId, Guid.NewGuid(), "TRY", 100m, 100m);
        var operations = new Operations();
        operations.Add(walletId, "retry", 100m, receipt);
        var journals = new Journals();
        var saves = new Saves();
        var handler = new TestDepositHandler(new Wallets(null), new Accounts(), journals, operations, saves, new Clock());

        Assert.Equal(receipt, await handler.Handle(new TestDepositCommand(walletId, 100m, "retry")));
        Assert.Equal(0, saves.Count);
        Assert.Empty(journals.Added);
    }

    [Fact]
    public async Task Handle_WhenKeyIsReusedForDifferentAmount_ShouldRejectWithoutSaving()
    {
        var walletId = Guid.NewGuid();
        var operations = new Operations();
        operations.Add(walletId, "retry", 100m,
            new TestDepositResult(walletId, Guid.NewGuid(), "TRY", 100m, 100m));
        var saves = new Saves();
        var handler = new TestDepositHandler(new Wallets(null), new Accounts(), new Journals(), operations, saves, new Clock());

        await Assert.ThrowsAsync<TestDepositIdempotencyConflictException>(() =>
            handler.Handle(new TestDepositCommand(walletId, 200m, "retry")));
        Assert.Equal(0, saves.Count);
    }

    private static Wallet NewWallet() => Wallet.Create(Guid.NewGuid(), Currency.FromCode("TRY"), Now);
    private static LedgerAccountSnapshot Snapshot(LedgerAccount account) =>
        new(account.Id, account.WalletId, account.Type, account.Purpose, account.Currency.Code, account.CreatedAtUtc);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }

    private sealed class Wallets(Wallet? wallet) : IWalletRepository
    {
        public Task<Wallet?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(wallet);
        public Task<Wallet?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(Guid owner, Currency currency, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Add(Wallet value) => throw new NotSupportedException();
    }

    private sealed class Accounts : ILedgerAccountRepository
    {
        public LedgerAccountSnapshot? Customer { get; set; }
        public LedgerAccountSnapshot? Funding { get; set; }
        public List<LedgerAccount> Added { get; } = [];
        public int Reads { get; private set; }
        public void Add(LedgerAccount account) => Added.Add(account);
        public Task<LedgerAccountSnapshot?> GetByWalletIdAsync(Guid id, CancellationToken cancellationToken = default)
        { Reads++; return Task.FromResult(Customer); }
        public Task<LedgerAccountSnapshot?> GetTestFundingAsync(Currency currency, CancellationToken cancellationToken = default)
        { Reads++; return Task.FromResult(Funding); }
    }

    private sealed class Journals : IJournalEntryRepository
    {
        public List<JournalEntry> Added { get; } = [];
        public void Add(JournalEntry journal) => Added.Add(journal);
        public Task<JournalEntrySnapshot?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Operations : ITestDepositOperationRepository
    {
        private readonly Dictionary<(Guid WalletId, string Key), TestDepositOperationSnapshot> _entries = [];
        public bool ReadAdded { get; init; } = true;
        public Task<TestDepositOperationSnapshot?> GetAsync(Guid walletId, string key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ReadAdded ? _entries.GetValueOrDefault((walletId, key)) : null);
        public void Add(Guid walletId, string key, decimal amount, TestDepositResult result) =>
            _entries.Add((walletId, key), new TestDepositOperationSnapshot(amount, result));
    }

    private sealed class Saves : IUnitOfWork
    {
        public int Count { get; private set; }
        public CancellationToken Token { get; private set; }
        public Exception? Failure { get; init; }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        { Count++; Token = cancellationToken; return Failure is null ? Task.CompletedTask : Task.FromException(Failure); }
    }
}
