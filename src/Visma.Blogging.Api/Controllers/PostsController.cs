using Microsoft.AspNetCore.Mvc;
using Visma.Blogging.Application.Abstractions;
using Visma.Blogging.Application.Blogging;

namespace Visma.Blogging.Api.Controllers;

/// <summary>
/// HTTP adapter for blog post operations.
/// </summary>
[ApiController]
[Route("")]
public sealed class PostsController : ControllerBase
{
    private const string IdempotencyKeyHeaderName = "Idempotency-Key";
    private readonly CreatePostEndpointService _createPostEndpoint;
    private readonly IQueryHandler<GetPostByIdQuery, PostResponse> _getPostByIdHandler;
    private readonly ILogger<PostsController> _logger;

    /// <summary>
    /// Creates the controller with application use case handlers.
    /// </summary>
    public PostsController(
        CreatePostEndpointService createPostEndpoint,
        IQueryHandler<GetPostByIdQuery, PostResponse> getPostByIdHandler,
        ILogger<PostsController> logger)
    {
        _createPostEndpoint = createPostEndpoint;
        _getPostByIdHandler = getPostByIdHandler;
        _logger = logger;
    }

    /// <summary>
    /// Creates a blog post and its author.
    /// </summary>
    [HttpPost("post", Name = "CreatePost")]
    [ProducesResponseType(typeof(PostResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PostResponse>> CreateAsync(
        [FromBody] CreatePostRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _createPostEndpoint.CreateAsync(
                request,
                Request.Headers[IdempotencyKeyHeaderName].FirstOrDefault(),
                cancellationToken)
            .ConfigureAwait(false);

        return result.ToPostCreatedResult(this);
    }

    /// <summary>
    /// Gets one post, optionally including author details.
    /// </summary>
    [HttpGet("post/{id:guid}", Name = "GetPostById")]
    [ProducesResponseType(typeof(PostResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PostResponse>> GetByIdAsync(
        [FromRoute] Guid id,
        [FromQuery] bool includeAuthor,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching post {PostId} with includeAuthor {IncludeAuthor}", id, includeAuthor);

        // includeAuthor is a read-side option; it does not affect the stored post.
        var result = await _getPostByIdHandler.HandleAsync(new GetPostByIdQuery(id, includeAuthor), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Fetched post {PostId}", id);
        }
        else
        {
            _logger.LogWarning("Post {PostId} lookup failed with {ErrorCode}", id, result.Error!.Code);
        }

        return result.ToGetPostResult(this);
    }
}
