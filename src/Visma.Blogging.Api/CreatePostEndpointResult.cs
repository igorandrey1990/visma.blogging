using Visma.Blogging.Application.Abstractions;
using Visma.Blogging.Application.Blogging;

namespace Visma.Blogging.Api;

/// <summary>
/// API-level outcome for creating a post.
/// </summary>
public sealed class CreatePostEndpointResult
{
    private CreatePostEndpointResult(
        CreatePostEndpointResultKind kind,
        PostResponse? response,
        ApplicationError? applicationError,
        string? location,
        string? problemTitle,
        string? problemType)
    {
        Kind = kind;
        Response = response;
        ApplicationError = applicationError;
        Location = location;
        ProblemTitle = problemTitle;
        ProblemType = problemType;
    }

    public CreatePostEndpointResultKind Kind { get; }

    public PostResponse? Response { get; }

    public ApplicationError? ApplicationError { get; }

    public string? Location { get; }

    public string? ProblemTitle { get; }

    public string? ProblemType { get; }

    public static CreatePostEndpointResult Created(PostResponse response, string location)
    {
        return new CreatePostEndpointResult(CreatePostEndpointResultKind.Created, response, null, location, null, null);
    }

    public static CreatePostEndpointResult Replayed(PostResponse response, string location)
    {
        return new CreatePostEndpointResult(CreatePostEndpointResultKind.Replayed, response, null, location, null, null);
    }

    public static CreatePostEndpointResult ApplicationFailure(ApplicationError error)
    {
        return new CreatePostEndpointResult(CreatePostEndpointResultKind.ApplicationFailure, null, error, null, null, null);
    }

    public static CreatePostEndpointResult InvalidIdempotencyKey()
    {
        return new CreatePostEndpointResult(
            CreatePostEndpointResultKind.BadRequest,
            null,
            null,
            null,
            "Idempotency key is too long.",
            "invalid_idempotency_key");
    }

    public static CreatePostEndpointResult IdempotencyInProgress()
    {
        return new CreatePostEndpointResult(
            CreatePostEndpointResultKind.Conflict,
            null,
            null,
            null,
            "A request with this idempotency key is already in progress.",
            "idempotency_key_in_progress");
    }

    public static CreatePostEndpointResult IdempotencyRequestMismatch()
    {
        return new CreatePostEndpointResult(
            CreatePostEndpointResultKind.Conflict,
            null,
            null,
            null,
            "The idempotency key was already used with a different request body.",
            "idempotency_key_reused");
    }
}
