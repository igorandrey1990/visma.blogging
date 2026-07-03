using Visma.Blogging.Domain;

namespace Visma.Blogging.Application.Blogging;

/// <summary>
/// Write-side port for blog persistence.
/// </summary>
public interface IPostCommandStore
{
    /// <summary>
    /// Persists a post and its author atomically.
    /// </summary>
    Task SaveAsync(Post post, Author author, CancellationToken cancellationToken);
}
