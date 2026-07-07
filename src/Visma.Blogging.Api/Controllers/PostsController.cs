using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Visma.Blogging.Application.Abstractions;
using Visma.Blogging.Application.Blogging;

namespace Visma.Blogging.Api.Controllers;

/// <summary>
/// HTTP adapter for blog post operations.
/// </summary>
[ApiController]
[ApiVersion(1.0)]
[Route("")]
// Supported response formats are declared at the adapter boundary; use cases return models, not JSON/XML.
[Produces("application/json", "application/xml")]
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
    [HttpPost("api/v{version:apiVersion}/post", Name = "CreatePostV1")]
    [MapToApiVersion(1.0)]
    [EnableRateLimiting("post-write")]
    [Consumes("application/json", "application/xml")]
    [ProducesResponseType(typeof(PostResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PostResponse>> CreateAsync(
        [FromBody] CreatePostRequest request,
        CancellationToken cancellationToken)
    {
        // Versioned and legacy routes share the same implementation to avoid behavior drift.
        return await CreateCoreAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Legacy route kept for backward compatibility with the original challenge endpoint.
    /// </summary>
    [HttpPost("post", Name = "CreatePost")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [EnableRateLimiting("post-write")]
    [Consumes("application/json", "application/xml")]
    public async Task<ActionResult<PostResponse>> CreateLegacyAsync(
        [FromBody] CreatePostRequest request,
        CancellationToken cancellationToken)
    {
        // Keep the challenge's original /post route working without exposing it as the preferred Swagger contract.
        return await CreateCoreAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ActionResult<PostResponse>> CreateCoreAsync(
        CreatePostRequest request,
        CancellationToken cancellationToken)
    {
        // Idempotency is an HTTP concern because clients express retry intent through a request header.
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
    [HttpGet("api/v{version:apiVersion}/post/{id:guid}", Name = "GetPostByIdV1")]
    [MapToApiVersion(1.0)]
    [EnableRateLimiting("post-read")]
    [ProducesResponseType(typeof(PostResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PostResponse>> GetByIdAsync(
        [FromRoute] Guid id,
        [FromQuery] bool includeAuthor,
        CancellationToken cancellationToken)
    {
        // Versioned and legacy reads also share one path so includeAuthor behaves identically everywhere.
        return await GetByIdCoreAsync(id, includeAuthor, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Legacy route kept for backward compatibility with the original challenge endpoint.
    /// </summary>
    [HttpGet("post/{id:guid}", Name = "GetPostById")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [EnableRateLimiting("post-read")]
    public async Task<ActionResult<PostResponse>> GetByIdLegacyAsync(
        [FromRoute] Guid id,
        [FromQuery] bool includeAuthor,
        CancellationToken cancellationToken)
    {
        return await GetByIdCoreAsync(id, includeAuthor, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ActionResult<PostResponse>> GetByIdCoreAsync(
        Guid id,
        bool includeAuthor,
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
