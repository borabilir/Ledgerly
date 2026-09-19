namespace Ledgerly.Application.Abstractions.Messaging;

public interface IOutboxMessageWriter
{
    void Add<TEvent>(
        Guid id,
        Guid aggregateId,
        string type,
        DateTimeOffset occurredAtUtc,
        TEvent payload)
        where TEvent : class;
}
