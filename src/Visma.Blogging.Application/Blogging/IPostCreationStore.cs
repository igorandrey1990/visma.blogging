using Visma.Blogging.Application.Messaging;
using Visma.Blogging.Domain;

namespace Visma.Blogging.Application.Blogging;

/// <summary>
/// Write-side port for atomically creating a post and recording its integration message.
/// </summary>
public interface IPostCreationStore
{
    /// <summary>
    /// Persists a post, its author snapshot, and its outbox message as one atomic write.
    /// </summary>
    Task SaveAsync(Post post, Author author, OutboxMessage outboxMessage, CancellationToken cancellationToken);
}
