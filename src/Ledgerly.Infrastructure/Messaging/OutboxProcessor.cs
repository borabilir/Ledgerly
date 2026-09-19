using Ledgerly.Application.Abstractions.Messaging;
using Ledgerly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ledgerly.Infrastructure.Messaging;

internal sealed class OutboxProcessor(
    LedgerlyDbContext dbContext,
    IIntegrationEventPublisher publisher,
    TimeProvider timeProvider,
    IOptions<OutboxOptions> options)
{
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        var messages = await dbContext.OutboxMessages
            .Where(message => message.ProcessedAtUtc == null)
            .OrderBy(message => message.OccurredAtUtc)
            .Take(options.Value.BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            message.AttemptCount++;
            try
            {
                await publisher.PublishAsync(new IntegrationEventMessage(
                    message.Id,
                    message.AggregateId,
                    message.Type,
                    message.Payload,
                    message.OccurredAtUtc), cancellationToken);
                message.ProcessedAtUtc = timeProvider.GetUtcNow();
                message.LastError = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                message.LastError = exception.Message.Length <= 2000
                    ? exception.Message
                    : exception.Message[..2000];
            }

            // Saving after each attempt makes the retry state durable. If publishing
            // succeeds but this save fails, the event can be published again later.
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return messages.Count;
    }
}
