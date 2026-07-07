using System.Text;
using RabbitMQ.Client;
using Visma.Blogging.Application.Messaging;

namespace Visma.Blogging.Infrastructure.Messaging;

/// <summary>
/// RabbitMQ publisher for integration events.
/// </summary>
public sealed class RabbitMqMessagePublisher : IRabbitMqMessagePublisher
{
    private readonly RabbitMqOptions _options;

    /// <summary>
    /// Creates the publisher.
    /// </summary>
    public RabbitMqMessagePublisher(RabbitMqOptions options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            ClientProvidedName = "visma-blogging-api"
        };

        await using var connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await DeclareTopologyAsync(channel, cancellationToken).ConfigureAwait(false);

        var body = Encoding.UTF8.GetBytes(message.PayloadJson);
        // Persistent delivery keeps messages durable on the broker when the queue is durable.
        var properties = new BasicProperties
        {
            AppId = "visma-blogging-api",
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = message.Id.ToString("D"),
            Timestamp = new AmqpTimestamp(message.OccurredAt.ToUnixTimeSeconds()),
            Type = message.Type,
            Headers = new Dictionary<string, object?>
            {
                ["message-version"] = message.Version
            }
        };

        await channel.BasicPublishAsync(
                _options.ExchangeName,
                _options.PostCreatedRoutingKey,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        // The publisher declares topology idempotently so a fresh local/docker environment works without manual setup.
        await channel.ExchangeDeclareAsync(
                _options.ExchangeName,
                ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await channel.ExchangeDeclareAsync(
                _options.DeadLetterExchangeName,
                ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await channel.QueueDeclareAsync(
                _options.PostCreatedQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    // Unprocessable consumer messages can be routed away from the main queue instead of looping forever.
                    ["x-dead-letter-exchange"] = _options.DeadLetterExchangeName
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await channel.QueueBindAsync(
                _options.PostCreatedQueueName,
                _options.ExchangeName,
                _options.PostCreatedRoutingKey,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
