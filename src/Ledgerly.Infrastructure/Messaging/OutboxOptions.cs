namespace Ledgerly.Infrastructure.Messaging;

internal sealed class OutboxOptions
{
    public const string SectionName = "Outbox";
    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 20;
    public int PollingIntervalMilliseconds { get; set; } = 1000;
}
