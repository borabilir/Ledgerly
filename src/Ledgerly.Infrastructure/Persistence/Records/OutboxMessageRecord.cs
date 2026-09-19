namespace Ledgerly.Infrastructure.Persistence.Records;

internal sealed class OutboxMessageRecord
{
    public Guid Id { get; set; }
    public Guid AggregateId { get; set; }
    public string Type { get; set; } = null!;
    public string Payload { get; set; } = null!;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
}
