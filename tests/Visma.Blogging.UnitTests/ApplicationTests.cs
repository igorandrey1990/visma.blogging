using Visma.Blogging.Application.Abstractions;
using Visma.Blogging.Application.Blogging;
using Visma.Blogging.Application.Messaging;
using Visma.Blogging.Domain;

namespace Visma.Blogging.UnitTests;

/// <summary>
/// Tests the application layer without ASP.NET Core, MongoDB, or RabbitMQ.
/// These are true unit tests: the use-case handlers are exercised through their ports,
/// and small fake implementations stand in for infrastructure adapters.
/// </summary>
public sealed class ApplicationTests
{
    [Fact]
    public async Task CreatePostCommandHandler_persists_valid_post()
    {
        // CapturingStore implements the application write port in memory.
        // That lets this test prove the handler creates the correct domain objects
        // and integration message without depending on MongoDB.
        var store = new CapturingStore();
        var handler = CreateHandler(store);
        var command = new CreatePostCommand("Title", "Description", "Content", new CreateAuthorCommand("Ada", "Lovelace"));

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value!.Id);
        Assert.Equal("Ada", result.Value.Author!.Name);

        // The handler should call the atomic creation port with all three write-side pieces:
        // post, author snapshot, and outbox message. The transaction itself is tested
        // in InfrastructureTests because transactions are a MongoDB adapter concern.
        Assert.NotNull(store.Post);
        Assert.NotNull(store.Author);
        Assert.NotNull(store.OutboxMessage);
        Assert.Equal("post.created.v1", store.OutboxMessage.Type);
    }

    [Fact]
    public async Task CreatePostCommandHandler_returns_validation_errors()
    {
        // Expected input problems are returned as Result failures instead of exceptions.
        // This keeps validation part of normal application flow and lets the API map it to 400.
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
        // The fake store simulates a persistence conflict, such as a duplicate key.
        // The handler translates that infrastructure-style failure into an application
        // conflict result so the API can return 409 without knowing about MongoDB.
        var handler = CreateHandler(new ThrowingStore());
        var command = new CreatePostCommand("Title", "Description", "Content", new CreateAuthorCommand("Ada", "Lovelace"));

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
    }

    [Fact]
    public async Task GetPostByIdQueryHandler_returns_not_found()
    {
        // A query returning null from the port means the application use case should
        // produce a NotFound result. The handler does not return ASP.NET Core NotFound;
        // that mapping belongs to the API layer.
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
        // includeAuthor is a read-side option. This test protects the contract that
        // the query can return the post without author details when the caller does
        // not ask for the larger read model.
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
        // The handler is built manually instead of using dependency injection.
        // Unit tests should stay small and explicit: each dependency is chosen by the test.
        return new CreatePostCommandHandler(
            new CreatePostCommandValidator(),
            store,
            new SequentialIdGenerator(),
            new FixedClock());
    }

    private sealed class CapturingStore : IPostCreationStore, IPostQueryStore
    {
        // This fake implements both write and read ports because application tests
        // only need to observe what the handler attempted to save or return.
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
        // A purpose-built fake is clearer than configuring a mocking framework here.
        // It documents the exact failure being simulated: the persistence adapter rejected the write.
        public Task SaveAsync(Post post, Author author, OutboxMessage outboxMessage, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("duplicate");
        }
    }

    private sealed class SequentialIdGenerator : IIdGenerator
    {
        // Deterministic IDs make assertions stable and keep tests independent from Guid.NewGuid().
        private int _value;

        public Guid NewId()
        {
            _value++;
            return Guid.Parse($"00000000-0000-0000-0000-{_value:000000000000}");
        }
    }

    private sealed class FixedClock : IClock
    {
        // A fixed clock removes time as a source of randomness.
        // That makes message timestamps and created-at values predictable.
        public DateTimeOffset UtcNow { get; } = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    }
}
