using Visma.Blogging.Domain;

namespace Visma.Blogging.Application.Blogging;

/// <summary>
/// Read-side port for blog queries.
/// </summary>
public interface IPostQueryStore
{
    /// <summary>
    /// Gets a post with optional author details.
    /// </summary>
    Task<PostDetails?> GetByIdAsync(PostId id, bool includeAuthor, CancellationToken cancellationToken);
}
