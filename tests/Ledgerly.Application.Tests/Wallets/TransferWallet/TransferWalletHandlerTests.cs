using Ledgerly.Application.Abstractions.Messaging;
using Ledgerly.Application.Abstractions.Persistence;
using Ledgerly.Application.Ledger;
using Ledgerly.Application.Wallets;
using Ledgerly.Application.Wallets.TransferWallet;
using Ledgerly.Domain.Ledger;
using Ledgerly.Domain.Wallets;

namespace Ledgerly.Application.Tests.Wallets.TransferWallet;

public sealed class TransferWalletHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_ShouldMoveBalanceAndCreateBalancedJournal(bool accountsExist)
    {
        var source = WalletWithBalance(100m);
        var destination = WalletWithBalance(20m);
        var wallets = new Wallets(source, destination);
        var accounts = new Accounts();
        if (accountsExist)
        {
            accounts.Seed(LedgerAccount.CreateForWallet(source.Id, source.Currency, Now));
            accounts.Seed(LedgerAccount.CreateForWallet(destination.Id, destination.Currency, Now));
        }
        var journals = new Journals();
        var transfers = new Transfers();
        var saves = new Saves();
        var events = new Events();
        var handler = new TransferWalletHandler(
            wallets, accounts, journals, transfers, saves, events, new Clock());

        var result = await handler.Handle(new TransferWalletCommand(source.Id, destination.Id, 40m, "transfer-1"));

        Assert.NotNull(result);
        Assert.Equal(60m, source.Balance);
        Assert.Equal(60m, destination.Balance);
        Assert.Equal(source.Balance, result.SourceBalance);
        Assert.Equal(destination.Balance, result.DestinationBalance);
        Assert.Equal("TRY", result.CurrencyCode);
        Assert.Equal(accountsExist ? 0 : 2, accounts.Added.Count);
        Assert.Equal(1, saves.Count);
        Assert.Equal(result, Assert.Single(transfers.Added).Result);
        var outboxMessage = Assert.Single(events.Messages);
        Assert.Equal(TransferCompletedIntegrationEvent.EventType, outboxMessage.Type);
        var integrationEvent = Assert.IsType<TransferCompletedIntegrationEvent>(outboxMessage.Payload);
        Assert.Equal(integrationEvent.EventId, outboxMessage.Id);
        Assert.Equal(result.TransferId, outboxMessage.AggregateId);
        Assert.Equal(result.TransferId, integrationEvent.TransferId);
        Assert.Equal(result.JournalEntryId, journals.Added.Single().Id);
        var sourceAccount = accounts.ForWallet(source.Id);
        var destinationAccount = accounts.ForWallet(destination.Id);
        var journal = Assert.Single(journals.Added);
        Assert.Equal(journal.Id, result.JournalEntryId);
        Assert.Collection(journal.Postings,
            posting => Assert.Equal(Posting.Create(sourceAccount.Id, PostingDirection.Debit, 40m), posting),
            posting => Assert.Equal(Posting.Create(destinationAccount.Id, PostingDirection.Credit, 40m), posting));
    }

    [Fact]
    public async Task Handle_WhenFundsAreInsufficient_ShouldRejectWithoutChangingEitherWallet()
    {
        var source = WalletWithBalance(10m);
        var destination = WalletWithBalance(20m);
        var accounts = new Accounts();
        var journals = new Journals();
        var saves = new Saves();
        var handler = new TransferWalletHandler(
            new Wallets(source, destination), accounts, journals, new Transfers(), saves, new Events(), new Clock());

        await Assert.ThrowsAsync<InsufficientFundsException>(() =>
            handler.Handle(new TransferWalletCommand(source.Id, destination.Id, 11m, "transfer-1")));

        Assert.Equal(10m, source.Balance);
        Assert.Equal(20m, destination.Balance);
        Assert.Equal(0, accounts.Reads);
        Assert.Empty(journals.Added);
        Assert.Equal(0, saves.Count);
    }

    [Fact]
    public async Task Handle_WhenWalletIsMissing_ShouldReturnNullWithoutChangingSource()
    {
        var source = WalletWithBalance(100m);
        var handler = new TransferWalletHandler(
            new Wallets(source), new Accounts(), new Journals(), new Transfers(), new Saves(), new Events(), new Clock());

        var result = await handler.Handle(new TransferWalletCommand(source.Id, Guid.NewGuid(), 10m, "transfer-1"));

        Assert.Null(result);
        Assert.Equal(100m, source.Balance);
    }

    [Fact]
    public async Task Handle_WhenWalletsAreSame_ShouldRejectBeforeReading()
    {
        var wallet = WalletWithBalance(100m);
        var wallets = new Wallets(wallet);
        var handler = new TransferWalletHandler(
            wallets, new Accounts(), new Journals(), new Transfers(), new Saves(), new Events(), new Clock());

        await Assert.ThrowsAsync<SameWalletTransferException>(() =>
            handler.Handle(new TransferWalletCommand(wallet.Id, wallet.Id, 10m, "transfer-1")));

        Assert.Equal(0, wallets.Reads);
        Assert.Equal(100m, wallet.Balance);
    }

    [Fact]
    public async Task Handle_WhenTheSameKeyAndPayloadWereCompleted_ShouldReturnStoredReceiptWithoutWriting()
    {
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var stored = new TransferWalletResult(
            Guid.NewGuid(), Guid.NewGuid(), sourceId, destinationId, "TRY", 40m, 60m, 40m);
        var transfers = new Transfers(new WalletTransferSnapshot(stored));
        var wallets = new Wallets();
        var saves = new Saves();
        var events = new Events();
        var handler = new TransferWalletHandler(
            wallets, new Accounts(), new Journals(), transfers, saves, events, new Clock());

        var result = await handler.Handle(
            new TransferWalletCommand(sourceId, destinationId, 40m, "transfer-1"));

        Assert.Equal(stored, result);
        Assert.Equal(0, wallets.Reads);
        Assert.Equal(0, saves.Count);
        Assert.Empty(events.Messages);
    }

    [Fact]
    public async Task Handle_WhenTheSameKeyHasDifferentPayload_ShouldRejectWithoutWriting()
    {
        var sourceId = Guid.NewGuid();
        var stored = new TransferWalletResult(
            Guid.NewGuid(), Guid.NewGuid(), sourceId, Guid.NewGuid(), "TRY", 40m, 60m, 40m);
        var wallets = new Wallets();
        var saves = new Saves();
        var handler = new TransferWalletHandler(
            wallets, new Accounts(), new Journals(),
            new Transfers(new WalletTransferSnapshot(stored)), saves, new Events(), new Clock());

        await Assert.ThrowsAsync<TransferIdempotencyConflictException>(() => handler.Handle(
            new TransferWalletCommand(sourceId, Guid.NewGuid(), 40m, "transfer-1")));

        Assert.Equal(0, wallets.Reads);
        Assert.Equal(0, saves.Count);
    }

    private static Wallet WalletWithBalance(decimal balance)
    {
        var wallet = Wallet.Create(Guid.NewGuid(), Currency.FromCode("TRY"), Now);
        if (balance > 0m) wallet.Credit(balance);
        return wallet;
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Wallets(params Wallet[] values) : IWalletRepository
    {
        private readonly Dictionary<Guid, Wallet> _wallets = values.ToDictionary(wallet => wallet.Id);
        public int Reads { get; private set; }
        public Task<Wallet?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(_wallets.GetValueOrDefault(id));
        }
        public Task<Wallet?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<bool> ExistsAsync(Guid owner, Currency currency, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public void Add(Wallet wallet) => throw new NotSupportedException();
    }

    private sealed class Accounts : ILedgerAccountRepository
    {
        private readonly Dictionary<Guid, LedgerAccountSnapshot> _accounts = [];
        public List<LedgerAccount> Added { get; } = [];
        public int Reads { get; private set; }
        public void Seed(LedgerAccount account) => _accounts.Add(account.WalletId!.Value, Snapshot(account));
        public LedgerAccountSnapshot ForWallet(Guid walletId) =>
            _accounts.GetValueOrDefault(walletId)
            ?? Snapshot(Assert.Single(Added, account => account.WalletId == walletId));
        public void Add(LedgerAccount account) => Added.Add(account);
        public Task<LedgerAccountSnapshot?> GetByWalletIdAsync(Guid walletId,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(_accounts.GetValueOrDefault(walletId));
        }
        public Task<LedgerAccountSnapshot?> GetTestFundingAsync(Currency currency,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        private static LedgerAccountSnapshot Snapshot(LedgerAccount account) =>
            new(account.Id, account.WalletId, account.Type, account.Purpose, account.Currency.Code, account.CreatedAtUtc);
    }

    private sealed class Journals : IJournalEntryRepository
    {
        public List<JournalEntry> Added { get; } = [];
        public void Add(JournalEntry journal) => Added.Add(journal);
        public Task<JournalEntrySnapshot?> GetByIdAsync(Guid id,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Transfers(WalletTransferSnapshot? stored = null) : IWalletTransferRepository
    {
        public List<(string Key, TransferWalletResult Result, DateTimeOffset CreatedAtUtc)> Added { get; } = [];

        public Task<WalletTransferSnapshot?> GetAsync(
            Guid sourceWalletId,
            string idempotencyKey,
            CancellationToken cancellationToken = default) => Task.FromResult(stored);

        public void Add(string idempotencyKey, TransferWalletResult result, DateTimeOffset createdAtUtc) =>
            Added.Add((idempotencyKey, result, createdAtUtc));
    }

    private sealed class Saves : IUnitOfWork
    {
        public int Count { get; private set; }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }

    private sealed class Events : IOutboxMessageWriter
    {
        public List<(Guid Id, Guid AggregateId, string Type, DateTimeOffset OccurredAtUtc, object Payload)> Messages { get; } = [];

        public void Add<TEvent>(
            Guid id,
            Guid aggregateId,
            string type,
            DateTimeOffset occurredAtUtc,
            TEvent payload)
            where TEvent : class
            => Messages.Add((id, aggregateId, type, occurredAtUtc, payload));
    }
}
