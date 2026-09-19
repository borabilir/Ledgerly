using System.Text;
using Ledgerly.Application.Abstractions.Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Ledgerly.Infrastructure.Messaging;

internal sealed class RabbitMqIntegrationEventPublisher(
    IOptions<RabbitMqOptions> options) : IIntegrationEventPublisher, IAsyncDisposable
{
    private readonly RabbitMqOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync(
        IntegrationEventMessage message,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureConnectedAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.PublishTimeoutSeconds));
            var properties = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = message.Id.ToString(),
                Type = message.Type,
                Timestamp = new AmqpTimestamp(message.OccurredAtUtc.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?>
                {
                    ["aggregate-id"] = message.AggregateId.ToString(),
                },
            };

            await _channel!.BasicPublishAsync(
                exchange: _options.ExchangeName,
                routingKey: message.Type,
                mandatory: true,
                basicProperties: properties,
                body: Encoding.UTF8.GetBytes(message.Payload),
                cancellationToken: timeout.Token);
        }
        catch
        {
            await ResetConnectionAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true } && _channel is { IsOpen: true })
        {
            return;
        }

        await ResetConnectionAsync();
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            ClientProvidedName = _options.ConnectionName,
            AutomaticRecoveryEnabled = false,
        };

        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true), cancellationToken);
        await _channel.ExchangeDeclareAsync(
            exchange: _options.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);
        await _channel.QueueDeclareAsync(
            queue: _options.TransferHistoryQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);
        await _channel.QueueBindAsync(
            queue: _options.TransferHistoryQueueName,
            exchange: _options.ExchangeName,
            routingKey: "wallet.transfer-completed.v1",
            arguments: null,
            cancellationToken: cancellationToken);
    }

    private async Task ResetConnectionAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await ResetConnectionAsync();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
