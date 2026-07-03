namespace Visma.Blogging.Application.Messaging;

/// <summary>
/// Application port for durably recording messages that should be published outside the service.
/// </summary>
public interface IOutboxWriter
{
    /// <summary>
    /// Saves a message for asynchronous publication.
    /// </summary>
    Task SaveAsync(OutboxMessage message, CancellationToken cancellationToken);
}
