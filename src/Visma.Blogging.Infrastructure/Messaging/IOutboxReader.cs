namespace Visma.Blogging.Infrastructure.Messaging;

/// <summary>
/// Infrastructure port for reading and updating publishable outbox records.
/// </summary>
public interface IOutboxReader
{
    /// <summary>
    /// Claims pending messages so one publisher instance can process them.
    /// </summary>
    Task<IReadOnlyCollection<OutboxMessageRecord>> ClaimPendingAsync(
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks one message as published.
    /// </summary>
    Task MarkPublishedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Marks one message as failed and schedules a later retry.
    /// </summary>
    Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken);
}
