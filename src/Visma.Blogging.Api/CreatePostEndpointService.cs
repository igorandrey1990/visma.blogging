using Visma.Blogging.Application.Abstractions;
using Visma.Blogging.Application.Blogging;

namespace Visma.Blogging.Api;

/// <summary>
/// Coordinates API-only create-post concerns before calling the application use case.
/// </summary>
public sealed class CreatePostEndpointService
{
    private const int MaxIdempotencyKeyLength = 200;
    private readonly ICommandHandler<CreatePostCommand, PostResponse> _createPostHandler;
    private readonly ICreatePostIdempotencyStore _idempotencyStore;
    private readonly ILogger<CreatePostEndpointService> _logger;

    /// <summary>
    /// Creates the service.
    /// </summary>
    public CreatePostEndpointService(
        ICommandHandler<CreatePostCommand, PostResponse> createPostHandler,
        ICreatePostIdempotencyStore idempotencyStore,
        ILogger<CreatePostEndpointService> logger)
    {
        _createPostHandler = createPostHandler;
        _idempotencyStore = idempotencyStore;
        _logger = logger;
    }

    /// <summary>
    /// Creates a post, optionally using an idempotency key to make client retries safe.
    /// </summary>
    public async Task<CreatePostEndpointResult> CreateAsync(
        CreatePostRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating post with title {Title}", request.Title);

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return await CreateWithoutIdempotencyAsync(request, cancellationToken).ConfigureAwait(false);
        }

        idempotencyKey = idempotencyKey.Trim();
        if (idempotencyKey.Length > MaxIdempotencyKeyLength)
        {
            // Bound the header size before storing it so clients cannot create oversized idempotency records.
            return CreatePostEndpointResult.InvalidIdempotencyKey();
        }

        return await CreateWithIdempotencyAsync(request, idempotencyKey, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CreatePostEndpointResult> CreateWithoutIdempotencyAsync(
        CreatePostRequest request,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteCreatePostAsync(request, cancellationToken).ConfigureAwait(false);

        return ToEndpointResult(result);
    }

    private async Task<CreatePostEndpointResult> CreateWithIdempotencyAsync(
        CreatePostRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // The hash binds an idempotency key to one exact request body. Reusing the key with different content is a conflict.
        var requestHash = IdempotencyRequestHasher.Hash(request);
        var idempotency = await _idempotencyStore.TryStartAsync(idempotencyKey, requestHash, cancellationToken)
            .ConfigureAwait(false);

        return idempotency.Status switch
        {
            CreatePostIdempotencyStatus.Started => await CompleteNewIdempotentRequestAsync(
                    request,
                    idempotencyKey,
                    requestHash,
                    cancellationToken)
                .ConfigureAwait(false),
            CreatePostIdempotencyStatus.Completed => CreatePostEndpointResult.Replayed(
                idempotency.Response!,
                idempotency.Location!),
            CreatePostIdempotencyStatus.InProgress => CreatePostEndpointResult.IdempotencyInProgress(),
            CreatePostIdempotencyStatus.RequestMismatch => CreatePostEndpointResult.IdempotencyRequestMismatch(),
            _ => throw new InvalidOperationException($"Unsupported idempotency status '{idempotency.Status}'.")
        };
    }

    private async Task<CreatePostEndpointResult> CompleteNewIdempotentRequestAsync(
        CreatePostRequest request,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        Result<PostResponse> result;
        try
        {
            result = await ExecuteCreatePostAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // If the use case throws, remove the in-progress marker so a later retry can start cleanly.
            await _idempotencyStore.RemoveAsync(idempotencyKey, requestHash, CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }

        if (!result.IsSuccess)
        {
            // Validation/domain failures are not cached as successful idempotent results.
            await _idempotencyStore.RemoveAsync(idempotencyKey, requestHash, cancellationToken).ConfigureAwait(false);
            return CreatePostEndpointResult.ApplicationFailure(result.Error!);
        }

        var value = result.Value!;
        var location = CreateLocation(value);
        await _idempotencyStore.CompleteAsync(idempotencyKey, requestHash, value, location, cancellationToken)
            .ConfigureAwait(false);

        return CreatePostEndpointResult.Created(value, location);
    }

    private async Task<Result<PostResponse>> ExecuteCreatePostAsync(
        CreatePostRequest request,
        CancellationToken cancellationToken)
    {
        // Keep HTTP contracts at the edge and pass an application command inward.
        var command = new CreatePostCommand(
            request.Title,
            request.Description,
            request.Content,
            request.Author is null ? null : new CreateAuthorCommand(request.Author.Name, request.Author.Surname));

        var result = await _createPostHandler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Created post {PostId}", result.Value!.Id);
        }
        else
        {
            _logger.LogWarning("Create post request failed with {ErrorCode}", result.Error!.Code);
        }

        return result;
    }

    private static CreatePostEndpointResult ToEndpointResult(Result<PostResponse> result)
    {
        return result.IsSuccess
            ? CreatePostEndpointResult.Created(result.Value!, CreateLocation(result.Value!))
            : CreatePostEndpointResult.ApplicationFailure(result.Error!);
    }

    private static string CreateLocation(PostResponse response)
    {
        return $"/api/v1/post/{response.Id:D}";
    }
}
