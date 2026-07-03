namespace Visma.Blogging.Infrastructure.Messaging;

/// <summary>
/// RabbitMQ publishing settings.
/// </summary>
public sealed class RabbitMqOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "RabbitMq";

    /// <summary>
    /// Enables the outbox publisher background service.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// RabbitMQ host name.
    /// </summary>
    public string HostName { get; set; } = "localhost";

    /// <summary>
    /// RabbitMQ AMQP port.
    /// </summary>
    public int Port { get; set; } = 5672;

    /// <summary>
    /// RabbitMQ virtual host.
    /// </summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>
    /// RabbitMQ username.
    /// </summary>
    public string UserName { get; set; } = "guest";

    /// <summary>
    /// RabbitMQ password.
    /// </summary>
    public string Password { get; set; } = "guest";

    /// <summary>
    /// Durable exchange used for integration events.
    /// </summary>
    public string ExchangeName { get; set; } = "visma.blogging";

    /// <summary>
    /// Durable queue bound for created post messages.
    /// </summary>
    public string PostCreatedQueueName { get; set; } = "visma.blogging.post-created";

    /// <summary>
    /// Routing key for post-created messages.
    /// </summary>
    public string PostCreatedRoutingKey { get; set; } = "post.created";

    /// <summary>
    /// Dead-letter exchange used by the queue.
    /// </summary>
    public string DeadLetterExchangeName { get; set; } = "visma.blogging.dlx";

    /// <summary>
    /// Number of outbox messages processed per polling cycle.
    /// </summary>
    public int PublisherBatchSize { get; set; } = 25;

    /// <summary>
    /// Delay between outbox polling cycles.
    /// </summary>
    public int PublisherPollingIntervalMilliseconds { get; set; } = 1000;

    /// <summary>
    /// How long a publisher claims an outbox record before another worker can retry it.
    /// </summary>
    public int OutboxLockSeconds { get; set; } = 30;
}
