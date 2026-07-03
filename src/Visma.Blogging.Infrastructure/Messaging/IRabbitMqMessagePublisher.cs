using Visma.Blogging.Application.Messaging;

namespace Visma.Blogging.Infrastructure.Messaging;

/// <summary>
/// Publishes integration messages to RabbitMQ.
/// </summary>
public interface IRabbitMqMessagePublisher
{
    /// <summary>
    /// Publishes one message.
    /// </summary>
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}
