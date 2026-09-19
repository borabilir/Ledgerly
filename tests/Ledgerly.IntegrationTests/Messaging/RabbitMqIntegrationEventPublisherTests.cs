using System.Text;
using Ledgerly.Application.Abstractions.Messaging;
using Ledgerly.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Ledgerly.IntegrationTests.Messaging;

public sealed class RabbitMqIntegrationEventPublisherTests
{
    private const string EventType = "wallet.transfer-completed.v1";

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Lab", "RabbitMqPublisher")]
    public async Task PublishAsync_WhenRabbitMqIsAvailable_ShouldDeliverMessageToBoundQueue()
    {
        var options = new RabbitMqOptions
        {
            HostName = "localhost",
            Port = 5672,
            UserName = "ledgerly",
            Password = "ledgerly_dev",
            ExchangeName = "ledgerly.events.integration-tests",
            TransferHistoryQueueName = "ledgerly.transfer-history.integration-tests",
            ConnectionName = "ledgerly-integration-tests",
        };
        var factory = CreateConnectionFactory(options);
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await DeclareAndPurgeTopologyAsync(channel, options);
        await using var publisher = new RabbitMqIntegrationEventPublisher(Options.Create(options));
        var eventId = Guid.NewGuid();
        var aggregateId = Guid.NewGuid();
        const string payload = "{\"transferId\":\"test-transfer\"}";
        var message = new IntegrationEventMessage(
            eventId,
            aggregateId,
            EventType,
            payload,
            DateTimeOffset.UtcNow);

        await publisher.PublishAsync(message);

        var deliveredMessage = await channel.BasicGetAsync(
            options.TransferHistoryQueueName,
            autoAck: true);
        Assert.NotNull(deliveredMessage);
        Assert.Equal(payload, Encoding.UTF8.GetString(deliveredMessage.Body.Span));
        Assert.Equal(eventId.ToString(), deliveredMessage.BasicProperties.MessageId);
        Assert.Equal(EventType, deliveredMessage.BasicProperties.Type);
        Assert.Equal("application/json", deliveredMessage.BasicProperties.ContentType);
        Assert.Equal(DeliveryModes.Persistent, deliveredMessage.BasicProperties.DeliveryMode);
    }

    private static ConnectionFactory CreateConnectionFactory(RabbitMqOptions options) => new()
    {
        HostName = options.HostName,
        Port = options.Port,
        UserName = options.UserName,
        Password = options.Password,
        VirtualHost = options.VirtualHost,
        ClientProvidedName = options.ConnectionName,
    };

    private static async Task DeclareAndPurgeTopologyAsync(
        IChannel channel,
        RabbitMqOptions options)
    {
        await channel.ExchangeDeclareAsync(
            options.ExchangeName,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false);
        await channel.QueueDeclareAsync(
            options.TransferHistoryQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false);
        await channel.QueueBindAsync(
            options.TransferHistoryQueueName,
            options.ExchangeName,
            EventType);
        await channel.QueuePurgeAsync(options.TransferHistoryQueueName);
    }
}
