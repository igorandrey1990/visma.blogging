namespace Visma.Blogging.Application.Blogging;

/// <summary>
/// Stores idempotency state for create-post requests.
/// </summary>
public interface ICreatePostIdempotencyStore
{
    /// <summary>
    /// Reserves a key or returns the previously completed response for that key.
    /// </summary>
    Task<CreatePostIdempotencyStartResult> TryStartAsync(
        string key,
        string requestHash,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks a reserved key as complete with the response that should be replayed.
    /// </summary>
    Task CompleteAsync(
        string key,
        string requestHash,
        PostResponse response,
        string location,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes an unfinished reservation when no side effect should be replayed.
    /// </summary>
    Task RemoveAsync(string key, string requestHash, CancellationToken cancellationToken);
}
