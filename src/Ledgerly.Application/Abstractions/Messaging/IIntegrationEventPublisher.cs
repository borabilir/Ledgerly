namespace Ledgerly.Application.Abstractions.Messaging;

public interface IIntegrationEventPublisher
{
    Task PublishAsync(
        IntegrationEventMessage message,
        CancellationToken cancellationToken = default);
}

public sealed record IntegrationEventMessage(
    Guid Id,
    Guid AggregateId,
    string Type,
    string Payload,
    DateTimeOffset OccurredAtUtc);
