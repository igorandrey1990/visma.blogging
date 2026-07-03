namespace Visma.Blogging.Application.Blogging;

/// <summary>
/// States returned when checking a post creation idempotency key.
/// </summary>
public enum CreatePostIdempotencyStatus
{
    /// <summary>
    /// This request reserved the key and should execute the create operation.
    /// </summary>
    Started,

    /// <summary>
    /// The same request already completed and should be replayed.
    /// </summary>
    Completed,

    /// <summary>
    /// Another request with the same key is still running.
    /// </summary>
    InProgress,

    /// <summary>
    /// The key was used before with different request content.
    /// </summary>
    RequestMismatch
}
