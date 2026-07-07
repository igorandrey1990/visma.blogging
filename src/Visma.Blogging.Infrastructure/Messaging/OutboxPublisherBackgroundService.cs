using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Visma.Blogging.Infrastructure.Messaging;

/// <summary>
/// Background worker that publishes MongoDB outbox messages to RabbitMQ.
/// </summary>
public sealed class OutboxPublisherBackgroundService : BackgroundService
{
    private readonly ILogger<OutboxPublisherBackgroundService> _logger;
    private readonly IOutboxReader _outbox;
    private readonly IRabbitMqMessagePublisher _publisher;
    private readonly RabbitMqOptions _options;

    /// <summary>
    /// Creates the worker.
    /// </summary>
    public OutboxPublisherBackgroundService(
        IOutboxReader outbox,
        IRabbitMqMessagePublisher publisher,
        RabbitMqOptions options,
        ILogger<OutboxPublisherBackgroundService> logger)
    {
        _outbox = outbox;
        _publisher = publisher;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("RabbitMQ outbox publisher is disabled.");
            return;
        }

        var pollingInterval = TimeSpan.FromMilliseconds(Math.Max(100, _options.PublisherPollingIntervalMilliseconds));
        var lockDuration = TimeSpan.FromSeconds(Math.Max(5, _options.OutboxLockSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Claiming locks records for this worker so multiple API instances can publish safely.
                var messages = await _outbox.ClaimPendingAsync(
                        Math.Max(1, _options.PublisherBatchSize),
                        lockDuration,
                        stoppingToken)
                    .ConfigureAwait(false);

                foreach (var record in messages)
                {
                    await PublishOneAsync(record, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Outbox publisher cycle failed.");
            }

            await Task.Delay(pollingInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task PublishOneAsync(OutboxMessageRecord record, CancellationToken cancellationToken)
    {
        try
        {
            await _publisher.PublishAsync(record.ToMessage(), cancellationToken).ConfigureAwait(false);
            // Marking the record after RabbitMQ accepts it keeps publication at-least-once.
            await _outbox.MarkPublishedAsync(record.Id, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Published outbox message {MessageId} of type {MessageType}.", record.Id, record.Type);
        }
        catch (Exception exception)
        {
            // Failed records are not discarded; they are scheduled for another attempt by the outbox store.
            await _outbox.MarkFailedAsync(record.Id, exception.Message, cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                exception,
                "Failed to publish outbox message {MessageId} of type {MessageType}.",
                record.Id,
                record.Type);
        }
    }
}
