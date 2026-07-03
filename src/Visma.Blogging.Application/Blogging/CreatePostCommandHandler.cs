using Visma.Blogging.Application.Abstractions;
using Visma.Blogging.Application.Messaging;
using Visma.Blogging.Domain;

namespace Visma.Blogging.Application.Blogging;

/// <summary>
/// Creates authors and posts through the write-side port.
/// </summary>
public sealed class CreatePostCommandHandler : ICommandHandler<CreatePostCommand, PostResponse>
{
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;
    private readonly IPostCreationStore _store;
    private readonly IValidator<CreatePostCommand> _validator;

    /// <summary>
    /// Creates the handler.
    /// </summary>
    public CreatePostCommandHandler(
        IValidator<CreatePostCommand> validator,
        IPostCreationStore store,
        IIdGenerator idGenerator,
        IClock clock)
    {
        _validator = validator;
        _store = store;
        _idGenerator = idGenerator;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Result<PostResponse>> HandleAsync(CreatePostCommand command, CancellationToken cancellationToken)
    {
        // Application validation gives callers a complete, field-level error response.
        var validation = _validator.Validate(command);
        if (!validation.IsValid)
        {
            return Result<PostResponse>.Failure(ApplicationError.Validation(validation.ToDictionary()));
        }

        try
        {
            // Domain factories are still used after validation so invariants are protected
            // even if this use case is invoked from a future non-HTTP entry point.
            var author = Author.Create(new AuthorId(_idGenerator.NewId()), command.Author!.Name, command.Author.Surname);
            var post = Post.Create(
                new PostId(_idGenerator.NewId()),
                author.Id,
                command.Title,
                command.Description,
                command.Content,
                _clock.UtcNow);

            var outboxMessage = OutboxMessageFactory.PostCreated(
                new PostCreatedIntegrationEvent(post.Id.Value, author.Id.Value, post.Title, post.CreatedAt),
                _clock.UtcNow);

            await _store.SaveAsync(post, author, outboxMessage, cancellationToken)
                .ConfigureAwait(false);

            // The API returns the author for the creation response because the caller supplied it.
            return Result<PostResponse>.Success(PostResponse.From(post, author));
        }
        catch (DomainValidationException exception)
        {
            // This is a defensive backstop for domain invariants, not the primary validation path.
            return Result<PostResponse>.Failure(ApplicationError.Validation(
                new Dictionary<string, string[]> { [exception.Field] = [exception.Message] }));
        }
        catch (InvalidOperationException exception)
        {
            return Result<PostResponse>.Failure(ApplicationError.Conflict("post_conflict", exception.Message));
        }
    }
}
