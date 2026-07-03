using Visma.Blogging.Application.Abstractions;
using Visma.Blogging.Application.Blogging;
using Visma.Blogging.Application.Messaging;
using Visma.Blogging.Domain;

namespace Visma.Blogging.UnitTests;

public sealed class ApplicationTests
{
    [Fact]
    public async Task CreatePostCommandHandler_persists_valid_post()
    {
        var store = new CapturingStore();
        var handler = CreateHandler(store);
        var command = new CreatePostCommand("Title", "Description", "Content", new CreateAuthorCommand("Ada", "Lovelace"));

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value!.Id);
        Assert.Equal("Ada", result.Value.Author!.Name);
        Assert.NotNull(store.Post);
        Assert.NotNull(store.Author);
        Assert.NotNull(store.OutboxMessage);
        Assert.Equal("post.created.v1", store.OutboxMessage.Type);
    }

    [Fact]
    public async Task CreatePostCommandHandler_returns_validation_errors()
    {
        var handler = CreateHandler(new CapturingStore());
        var command = new CreatePostCommand("", "Description", "Content", null);

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Contains("title", result.Error.Details.Keys);
        Assert.Contains("author", result.Error.Details.Keys);
    }

    [Fact]
    public async Task CreatePostCommandHandler_maps_conflicts()
    {
        var handler = CreateHandler(new ThrowingStore());
        var command = new CreatePostCommand("Title", "Description", "Content", new CreateAuthorCommand("Ada", "Lovelace"));

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
    }

    [Fact]
    public async Task GetPostByIdQueryHandler_returns_not_found()
    {
        var handler = new GetPostByIdQueryHandler(new GetPostByIdQueryValidator(), new CapturingStore());

        var result = await handler.HandleAsync(new GetPostByIdQuery(Guid.NewGuid(), IncludeAuthor: true), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task GetPostByIdQueryHandler_rejects_empty_id()
    {
        var handler = new GetPostByIdQueryHandler(new GetPostByIdQueryValidator(), new CapturingStore());

        var result = await handler.HandleAsync(new GetPostByIdQuery(Guid.Empty, IncludeAuthor: false), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Contains("id", result.Error.Details.Keys);
    }

    [Fact]
    public async Task GetPostByIdQueryHandler_returns_post_without_author_when_not_requested()
    {
        var postId = Guid.NewGuid();
        var author = Author.Create(new AuthorId(Guid.NewGuid()), "Ada", "Lovelace");
        var post = Post.Create(new PostId(postId), author.Id, "Title", "Description", "Content", DateTimeOffset.UtcNow);
        var store = new CapturingStore { Details = new PostDetails(post, null) };
        var handler = new GetPostByIdQueryHandler(new GetPostByIdQueryValidator(), store);

        var result = await handler.HandleAsync(new GetPostByIdQuery(postId, IncludeAuthor: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Author);
    }

    private static CreatePostCommandHandler CreateHandler(IPostCreationStore store)
    {
        return new CreatePostCommandHandler(
            new CreatePostCommandValidator(),
            store,
            new SequentialIdGenerator(),
            new FixedClock());
    }

    private sealed class CapturingStore : IPostCreationStore, IPostQueryStore
    {
        public Post? Post { get; private set; }

        public Author? Author { get; private set; }

        public OutboxMessage? OutboxMessage { get; private set; }

        public PostDetails? Details { get; init; }

        public Task SaveAsync(Post post, Author author, OutboxMessage outboxMessage, CancellationToken cancellationToken)
        {
            Post = post;
            Author = author;
            OutboxMessage = outboxMessage;
            return Task.CompletedTask;
        }

        public Task<PostDetails?> GetByIdAsync(PostId id, bool includeAuthor, CancellationToken cancellationToken)
        {
            return Task.FromResult(Details);
        }
    }

    private sealed class ThrowingStore : IPostCreationStore
    {
        public Task SaveAsync(Post post, Author author, OutboxMessage outboxMessage, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("duplicate");
        }
    }

    private sealed class SequentialIdGenerator : IIdGenerator
    {
        private int _value;

        public Guid NewId()
        {
            _value++;
            return Guid.Parse($"00000000-0000-0000-0000-{_value:000000000000}");
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    }
}
