using Visma.Blogging.Application.Abstractions;
using Visma.Blogging.Domain;

namespace Visma.Blogging.Application.Blogging;

/// <summary>
/// Fetches posts through the read-side port.
/// </summary>
public sealed class GetPostByIdQueryHandler : IQueryHandler<GetPostByIdQuery, PostResponse>
{
    private readonly IPostQueryStore _store;
    private readonly IValidator<GetPostByIdQuery> _validator;

    /// <summary>
    /// Creates the handler.
    /// </summary>
    public GetPostByIdQueryHandler(IValidator<GetPostByIdQuery> validator, IPostQueryStore store)
    {
        _validator = validator;
        _store = store;
    }

    /// <inheritdoc />
    public async Task<Result<PostResponse>> HandleAsync(GetPostByIdQuery query, CancellationToken cancellationToken)
    {
        var validation = _validator.Validate(query);
        if (!validation.IsValid)
        {
            return Result<PostResponse>.Failure(ApplicationError.Validation(validation.ToDictionary()));
        }

        var details = await _store.GetByIdAsync(new PostId(query.Id), query.IncludeAuthor, cancellationToken)
            .ConfigureAwait(false);

        // The query store uses null to represent absence; the application maps it to an explicit result.
        if (details is null)
        {
            return Result<PostResponse>.Failure(ApplicationError.NotFound("post_not_found", $"Post '{query.Id:D}' was not found."));
        }

        return Result<PostResponse>.Success(PostResponse.From(details.Post, details.Author));
    }
}
