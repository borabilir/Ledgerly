using Ledgerly.Application.Abstractions.Messaging;
using Microsoft.Extensions.Logging;

namespace Ledgerly.Infrastructure.Messaging;

internal sealed class LoggingIntegrationEventPublisher(
    ILogger<LoggingIntegrationEventPublisher> logger) : IIntegrationEventPublisher
{
    public Task PublishAsync(
        IntegrationEventMessage message,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Integration event {EventType} with id {EventId} for aggregate {AggregateId} published by the baseline logging publisher: {Payload}",
            message.Type,
            message.Id,
            message.AggregateId,
            message.Payload);

        return Task.CompletedTask;
    }
}
