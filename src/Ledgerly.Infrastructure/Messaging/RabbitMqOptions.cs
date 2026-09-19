namespace Ledgerly.Infrastructure.Messaging;

internal sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public bool Enabled { get; set; }
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string ConnectionName { get; set; } = "ledgerly-api";
    public string ExchangeName { get; set; } = "ledgerly.events";
    public string TransferHistoryQueueName { get; set; } = "ledgerly.transfer-history";
    public int PublishTimeoutSeconds { get; set; } = 10;
}
