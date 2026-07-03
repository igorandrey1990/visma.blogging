namespace Visma.Blogging.Application.Blogging;

/// <summary>
/// Result of attempting to reserve or replay an idempotent post creation.
/// </summary>
public sealed class CreatePostIdempotencyStartResult
{
    private CreatePostIdempotencyStartResult(
        CreatePostIdempotencyStatus status,
        PostResponse? response,
        string? location)
    {
        Status = status;
        Response = response;
        Location = location;
    }

    /// <summary>
    /// Current state for the idempotency key.
    /// </summary>
    public CreatePostIdempotencyStatus Status { get; }

    /// <summary>
    /// Previously completed response, when the key can be replayed.
    /// </summary>
    public PostResponse? Response { get; }

    /// <summary>
    /// Previously returned resource location, when the key can be replayed.
    /// </summary>
    public string? Location { get; }

    /// <summary>
    /// The caller owns this key and should continue processing the command.
    /// </summary>
    public static CreatePostIdempotencyStartResult Started()
    {
        return new CreatePostIdempotencyStartResult(CreatePostIdempotencyStatus.Started, null, null);
    }

    /// <summary>
    /// The same request already completed and can be replayed.
    /// </summary>
    public static CreatePostIdempotencyStartResult Completed(PostResponse response, string location)
    {
        return new CreatePostIdempotencyStartResult(CreatePostIdempotencyStatus.Completed, response, location);
    }

    /// <summary>
    /// The same key is already being processed by another request.
    /// </summary>
    public static CreatePostIdempotencyStartResult InProgress()
    {
        return new CreatePostIdempotencyStartResult(CreatePostIdempotencyStatus.InProgress, null, null);
    }

    /// <summary>
    /// The key was reused with different request content.
    /// </summary>
    public static CreatePostIdempotencyStartResult RequestMismatch()
    {
        return new CreatePostIdempotencyStartResult(CreatePostIdempotencyStatus.RequestMismatch, null, null);
    }
}
