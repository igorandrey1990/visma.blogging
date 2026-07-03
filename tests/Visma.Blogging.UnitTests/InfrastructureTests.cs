using MongoDB.Bson;
using MongoDB.Driver;
using Visma.Blogging.Application.Blogging;
using Visma.Blogging.Application.Messaging;
using Visma.Blogging.Domain;
using Visma.Blogging.Infrastructure.Messaging;
using Visma.Blogging.Infrastructure.Persistence;

namespace Visma.Blogging.UnitTests;

public sealed class InfrastructureTests
{
    [Fact]
    public async Task Retry_policy_retries_transient_failures()
    {
        var attempts = 0;
        var policy = new MongoRetryPolicy(new MongoBlogStoreOptions
        {
            RetryMaxAttempts = 3,
            RetryBaseDelayMilliseconds = 1
        });

        await policy.ExecuteAsync(
            _ =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new TimeoutException("temporary timeout");
                }

                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Store_saves_post_and_author_snapshot()
    {
        await using var fixture = await MongoStoreFixture.CreateAsync();
        var store = fixture.Store;
        var author = Author.Create(new AuthorId(Guid.NewGuid()), "Ada", "Lovelace");
        var post = Post.Create(new PostId(Guid.NewGuid()), author.Id, "Title", "Description", "Content", DateTimeOffset.UtcNow);

        await store.SaveAsync(post, author, CancellationToken.None);
        var details = await store.GetByIdAsync(post.Id, includeAuthor: true, CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal(author.Id, details!.Author!.Id);
        Assert.Equal(post.Id, details.Post.Id);
    }

    [Fact]
    public async Task Store_saves_post_and_outbox_message_in_one_transaction()
    {
        await using var fixture = await MongoStoreFixture.CreateAsync();
        var store = fixture.Store;
        var author = Author.Create(new AuthorId(Guid.NewGuid()), "Ada", "Lovelace");
        var post = Post.Create(new PostId(Guid.NewGuid()), author.Id, "Title", "Description", "Content", DateTimeOffset.UtcNow);
        var message = CreateOutboxMessage(post, author);

        await store.SaveAsync(post, author, message, CancellationToken.None);

        Assert.Equal(1, await fixture.CountPostsAsync());
        Assert.Equal(1, await fixture.CountOutboxAsync("pending"));
    }

    [Fact]
    public async Task Store_does_not_add_outbox_message_when_transaction_fails()
    {
        await using var fixture = await MongoStoreFixture.CreateAsync();
        var store = fixture.Store;
        var author = Author.Create(new AuthorId(Guid.NewGuid()), "Ada", "Lovelace");
        var post = Post.Create(new PostId(Guid.NewGuid()), author.Id, "Title", "Description", "Content", DateTimeOffset.UtcNow);

        await store.SaveAsync(post, author, CreateOutboxMessage(post, author), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(post, author, CreateOutboxMessage(post, author), CancellationToken.None));

        Assert.Equal(1, await fixture.CountPostsAsync());
        Assert.Equal(1, await fixture.CountOutboxAsync("pending"));
    }

    [Fact]
    public async Task Store_omits_author_when_not_requested()
    {
        await using var fixture = await MongoStoreFixture.CreateAsync();
        var store = fixture.Store;
        var author = Author.Create(new AuthorId(Guid.NewGuid()), "Ada", "Lovelace");
        var post = Post.Create(new PostId(Guid.NewGuid()), author.Id, "Title", "Description", "Content", DateTimeOffset.UtcNow);

        await store.SaveAsync(post, author, CancellationToken.None);
        var details = await store.GetByIdAsync(post.Id, includeAuthor: false, CancellationToken.None);

        Assert.NotNull(details);
        Assert.Null(details!.Author);
    }

    [Fact]
    public async Task Store_rejects_duplicate_post()
    {
        await using var fixture = await MongoStoreFixture.CreateAsync();
        var store = fixture.Store;
        var author = Author.Create(new AuthorId(Guid.NewGuid()), "Ada", "Lovelace");
        var post = Post.Create(new PostId(Guid.NewGuid()), author.Id, "Title", "Description", "Content", DateTimeOffset.UtcNow);

        await store.SaveAsync(post, author, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(post, author, CancellationToken.None));
    }

    [Fact]
    public async Task Store_is_safe_for_concurrent_writes()
    {
        await using var fixture = await MongoStoreFixture.CreateAsync();
        var store = fixture.Store;
        var tasks = Enumerable.Range(0, 20).Select(index =>
        {
            var author = Author.Create(new AuthorId(Guid.NewGuid()), $"Name {index}", $"Surname {index}");
            var post = Post.Create(new PostId(Guid.NewGuid()), author.Id, $"Title {index}", "Description", "Content", DateTimeOffset.UtcNow);
            return store.SaveAsync(post, author, CancellationToken.None);
        });

        await Task.WhenAll(tasks);

        Assert.Equal(20, await fixture.CountPostsAsync());
    }

    [Fact]
    public async Task Idempotency_store_replays_completed_response()
    {
        await using var fixture = await MongoStoreFixture.CreateAsync();
        var key = Guid.NewGuid().ToString("N");
        var hash = "request-hash";
        var response = new PostResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Title",
            "Description",
            "Content",
            DateTimeOffset.UtcNow,
            new AuthorResponse(Guid.NewGuid(), "Ada", "Lovelace"));

        var started = await fixture.IdempotencyStore.TryStartAsync(key, hash, CancellationToken.None);
        await fixture.IdempotencyStore.CompleteAsync(key, hash, response, $"/post/{response.Id:D}", CancellationToken.None);
        var replay = await fixture.IdempotencyStore.TryStartAsync(key, hash, CancellationToken.None);

        Assert.Equal(CreatePostIdempotencyStatus.Started, started.Status);
        Assert.Equal(CreatePostIdempotencyStatus.Completed, replay.Status);
        Assert.Equal(response.Id, replay.Response!.Id);
        Assert.Equal($"/post/{response.Id:D}", replay.Location);
    }

    [Fact]
    public async Task Idempotency_store_rejects_same_key_with_different_hash()
    {
        await using var fixture = await MongoStoreFixture.CreateAsync();
        var key = Guid.NewGuid().ToString("N");

        await fixture.IdempotencyStore.TryStartAsync(key, "first-hash", CancellationToken.None);
        var result = await fixture.IdempotencyStore.TryStartAsync(key, "second-hash", CancellationToken.None);

        Assert.Equal(CreatePostIdempotencyStatus.RequestMismatch, result.Status);
    }

    [Fact]
    public async Task Outbox_store_claims_and_marks_message_as_published()
    {
        await using var fixture = await MongoStoreFixture.CreateAsync();
        var message = new OutboxMessage(
            Guid.NewGuid(),
            "post.created.v1",
            1,
            DateTimeOffset.UtcNow,
            """{"postId":"00000000-0000-0000-0000-000000000001"}""");

        await fixture.OutboxStore.SaveAsync(message, CancellationToken.None);
        var claimed = await fixture.OutboxStore.ClaimPendingAsync(10, TimeSpan.FromSeconds(30), CancellationToken.None);
        await fixture.OutboxStore.MarkPublishedAsync(message.Id, CancellationToken.None);

        Assert.Single(claimed);
        Assert.Equal(message.Id, claimed.Single().Id);
        Assert.Equal(1, await fixture.CountOutboxAsync("published"));
    }

    private sealed class MongoStoreFixture : IAsyncDisposable
    {
        private const string ConnectionString = "mongodb://localhost:27017/?directConnection=true";
        private readonly IMongoClient _client;
        private readonly string _databaseName;

        private MongoStoreFixture(
            IMongoClient client,
            string databaseName,
            MongoBlogStore store,
            MongoCreatePostIdempotencyStore idempotencyStore,
            MongoOutboxStore outboxStore)
        {
            _client = client;
            _databaseName = databaseName;
            Store = store;
            IdempotencyStore = idempotencyStore;
            OutboxStore = outboxStore;
        }

        public MongoBlogStore Store { get; }

        public MongoCreatePostIdempotencyStore IdempotencyStore { get; }

        public MongoOutboxStore OutboxStore { get; }

        public static async Task<MongoStoreFixture> CreateAsync()
        {
            var databaseName = $"visma_blogging_tests_{Guid.NewGuid():N}";
            var client = new MongoClient(ConnectionString);
            await client.GetDatabase("admin")
                .RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1))
                .ConfigureAwait(false);

            var options = new MongoBlogStoreOptions
            {
                ConnectionString = ConnectionString,
                DatabaseName = databaseName,
                PostsCollectionName = "posts",
                IdempotencyCollectionName = "idempotency",
                OutboxCollectionName = "outbox",
                RetryBaseDelayMilliseconds = 1
            };
            var retryPolicy = new MongoRetryPolicy(options);

            return new MongoStoreFixture(
                client,
                databaseName,
                new MongoBlogStore(client, options, retryPolicy),
                new MongoCreatePostIdempotencyStore(client, options, retryPolicy),
                new MongoOutboxStore(client, options, retryPolicy));
        }

        public async Task<long> CountPostsAsync()
        {
            return await _client.GetDatabase(_databaseName)
                .GetCollection<object>("posts")
                .CountDocumentsAsync(FilterDefinition<object>.Empty)
                .ConfigureAwait(false);
        }

        public async Task<long> CountOutboxAsync(string status)
        {
            var filter = Builders<object>.Filter.Eq("Status", status);

            return await _client.GetDatabase(_databaseName)
                .GetCollection<object>("outbox")
                .CountDocumentsAsync(filter)
                .ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            await _client.DropDatabaseAsync(_databaseName).ConfigureAwait(false);
        }
    }

    private static OutboxMessage CreateOutboxMessage(Post post, Author author)
    {
        return OutboxMessageFactory.PostCreated(
            new PostCreatedIntegrationEvent(post.Id.Value, author.Id.Value, post.Title, post.CreatedAt),
            DateTimeOffset.UtcNow);
    }
}
