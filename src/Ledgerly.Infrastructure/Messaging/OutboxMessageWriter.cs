using System.Text.Json;
using Ledgerly.Application.Abstractions.Messaging;
using Ledgerly.Infrastructure.Persistence;
using Ledgerly.Infrastructure.Persistence.Records;

namespace Ledgerly.Infrastructure.Messaging;

internal sealed class OutboxMessageWriter(LedgerlyDbContext dbContext) : IOutboxMessageWriter
{
    public void Add<TEvent>(
        Guid id,
        Guid aggregateId,
        string type,
        DateTimeOffset occurredAtUtc,
        TEvent payload)
        where TEvent : class =>
        dbContext.OutboxMessages.Add(new OutboxMessageRecord
        {
            Id = id,
            AggregateId = aggregateId,
            Type = type,
            Payload = JsonSerializer.Serialize(payload),
            OccurredAtUtc = occurredAtUtc,
        });
}
