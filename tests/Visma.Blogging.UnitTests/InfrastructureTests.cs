using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Visma.Blogging.Application.Blogging;
using Visma.Blogging.Application.Messaging;
using Visma.Blogging.Domain;
using Visma.Blogging.Infrastructure.Messaging;
using Visma.Blogging.Infrastructure.Persistence;

namespace Visma.Blogging.UnitTests;

/// <summary>
/// Tests infrastructure adapters against a real local MongoDB container.
/// These are slower than pure unit tests, but they prove behavior that fake tests cannot:
/// transactions, duplicate keys, MongoDB query behavior, retry integration, and outbox state changes.
/// </summary>
public sealed class InfrastructureTests
{
    [Fact]
    public async Task Retry_policy_retries_transient_failures()
    {
        // The retry policy is tested with a deterministic timeout failure instead of
        // forcing MongoDB to fail. This keeps the test fast while proving retry control flow.
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
        // This uses the real MongoBlogStore to verify the document can round-trip:
        // domain object -> Mongo document -> domain/read model.
        await using var fixture = await MongoStoreFixture.CreateAsync();
        var store = fixture.Store;
        var author = Author.Create(new AuthorId(Guid.NewGuid()), "Ada", "Lovelace");
        var post = Post.Create(new PostId(Guid.NewGuid()), author.Id, "Title", "Description", "Content", DateTimeOffset.UtcNow);

        await store.SaveAsync(post, author, CreateOutboxMessage(post, author), CancellationToken.None);
        var details = await store.GetByIdAsync(post.Id, includeAuthor: true, CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal(author.Id, details!.Author!.Id);
        Assert.Equal(post.Id, details.Post.Id);
    }

    [Fact]
    public async Task Store_saves_post_and_outbox_message_in_one_transaction()
    {
        // This is the happy-path transactional outbox test. The post and the outbox
        // message should both exist after the single MongoDB transaction commits.
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
        // This protects the main reason we chose transactional outbox:
        // if the post insert fails, the outbox insert must not be committed either.
        // The duplicate post ID forces the transaction to fail.
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
        // The persistence adapter supports a read-side option that can omit author details.
        // This mirrors GET /post/{id} versus GET /post/{id}?includeAuthor=true.
        await using var fixture = await MongoStoreFixture.CreateAsync();
        var store = fixture.Store;
        var author = Author.Create(new AuthorId(Guid.NewGuid()), "Ada", "Lovelace");
        var post = Post.Create(new PostId(Guid.NewGuid()), author.Id, "Title", "Description", "Content", DateTimeOffset.UtcNow);

        await store.SaveAsync(post, author, CreateOutboxMessage(post, author), CancellationToken.None);
        var details = await store.GetByIdAsync(post.Id, includeAuthor: false, CancellationToken.None);

        Assert.NotNull(details);
        Assert.Null(details!.Author);
    }

    [Fact]
    public async Task Store_rejects_duplicate_post()
    {
        // MongoDB enforces uniqueness through the document _id.
        // The adapter translates MongoDB's duplicate-key error into InvalidOperationException,
        // which the application layer maps to a conflict result.
        await using var fixture = await MongoStoreFixture.CreateAsync();
        var store = fixture.Store;
        var author = Author.Create(new AuthorId(Guid.NewGuid()), "Ada", "Lovelace");
        var post = Post.Create(new PostId(Guid.NewGuid()), author.Id, "Title", "Description", "Content", DateTimeOffset.UtcNow);

        await store.SaveAsync(post, author, CreateOutboxMessage(post, author), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(post, author, CreateOutboxMessage(post, author), CancellationToken.None));
    }

    [Fact]
    public async Task Store_is_safe_for_concurrent_writes()
    {
        // This is a lightweight concurrency smoke test. It verifies the Mongo adapter
        // can handle many independent writes without shared in-memory state or race bugs.
        await using var fixture = await MongoStoreFixture.CreateAsync();
        var store = fixture.Store;
        var tasks = Enumerable.Range(0, 20).Select(index =>
        {
            var author = Author.Create(new AuthorId(Guid.NewGuid()), $"Name {index}", $"Surname {index}");
            var post = Post.Create(new PostId(Guid.NewGuid()), author.Id, $"Title {index}", "Description", "Content", DateTimeOffset.UtcNow);
            return store.SaveAsync(post, author, CreateOutboxMessage(post, author), CancellationToken.None);
        });

        await Task.WhenAll(tasks);

        Assert.Equal(20, await fixture.CountPostsAsync());
    }

    [Fact]
    public async Task Idempotency_store_replays_completed_response()
    {
        // Idempotency state is persisted in MongoDB so retries are safe even if the API
        // process restarts. This test proves a completed key can replay the saved response.
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
        // Reusing an idempotency key with a different body is dangerous because the client
        // might accidentally hide a different operation behind an old key. We reject it.
        await using var fixture = await MongoStoreFixture.CreateAsync();
        var key = Guid.NewGuid().ToString("N");

        await fixture.IdempotencyStore.TryStartAsync(key, "first-hash", CancellationToken.None);
        var result = await fixture.IdempotencyStore.TryStartAsync(key, "second-hash", CancellationToken.None);

        Assert.Equal(CreatePostIdempotencyStatus.RequestMismatch, result.Status);
    }

    [Fact]
    public async Task Outbox_store_claims_and_marks_message_as_published()
    {
        // The background publisher uses this claim/publish state machine.
        // Claiming prevents two publisher instances from publishing the same message at once,
        // and MarkPublished records that RabbitMQ accepted the message.
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

    [Fact]
    public async Task Mongo_index_initializer_creates_operational_and_ttl_indexes()
    {
        // Production systems need indexes for background worker queries and retention.
        // This verifies the Infrastructure startup initializer creates those MongoDB indexes.
        await using var fixture = await MongoStoreFixture.CreateAsync();

        await fixture.CreateIndexesAsync();

        Assert.True(await fixture.IndexExistsAsync("logs", "logs_timestamp_ttl"));
        Assert.True(await fixture.IndexExistsAsync("logs", "logs_trace_id"));
        Assert.True(await fixture.IndexExistsAsync("idempotency", "idempotency_created_at_ttl"));
        Assert.True(await fixture.IndexExistsAsync("outbox", "outbox_publishable"));
        Assert.True(await fixture.IndexExistsAsync("outbox", "outbox_published_at_ttl"));
    }

    private sealed class MongoStoreFixture : IAsyncDisposable
    {
        // Each test gets a unique database. That isolates tests from one another while
        // reusing the same Docker MongoDB instance for speed.
        private const string ConnectionString = "mongodb://localhost:27017/?directConnection=true";
        private readonly IMongoClient _client;
        private readonly string _databaseName;

        private MongoStoreFixture(
            IMongoClient client,
            string databaseName,
            MongoBlogStore store,
            MongoCreatePostIdempotencyStore idempotencyStore,
            MongoOutboxStore outboxStore,
            MongoBlogStoreOptions options,
            MongoRetryPolicy retryPolicy)
        {
            _client = client;
            _databaseName = databaseName;
            Store = store;
            IdempotencyStore = idempotencyStore;
            OutboxStore = outboxStore;
            Options = options;
            RetryPolicy = retryPolicy;
        }

        public MongoBlogStore Store { get; }

        public MongoCreatePostIdempotencyStore IdempotencyStore { get; }

        public MongoOutboxStore OutboxStore { get; }

        public MongoBlogStoreOptions Options { get; }

        public MongoRetryPolicy RetryPolicy { get; }

        public static async Task<MongoStoreFixture> CreateAsync()
        {
            // MongoDB transactions require the Docker MongoDB instance to run as a replica set.
            // The ping catches missing/stopped MongoDB early with a clear setup failure.
            var databaseName = $"visma_blogging_tests_{Guid.NewGuid():N}";
            var client = new MongoClient(ConnectionString);
            await client.GetDatabase("admin")
                .RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1))
                .ConfigureAwait(false);

            var options = new MongoBlogStoreOptions
            {
                // Collection names are explicit so tests can count documents directly.
                // This mirrors production configuration without sharing production data.
                ConnectionString = ConnectionString,
                DatabaseName = databaseName,
                PostsCollectionName = "posts",
                LogsCollectionName = "logs",
                IdempotencyCollectionName = "idempotency",
                OutboxCollectionName = "outbox",
                RetryBaseDelayMilliseconds = 1,
                LogsRetentionDays = 7,
                IdempotencyRetentionHours = 24,
                PublishedOutboxRetentionDays = 7
            };
            var retryPolicy = new MongoRetryPolicy(options);

            return new MongoStoreFixture(
                client,
                databaseName,
                new MongoBlogStore(client, options, retryPolicy),
                new MongoCreatePostIdempotencyStore(client, options, retryPolicy),
                new MongoOutboxStore(client, options, retryPolicy),
                options,
                retryPolicy);
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

        public async Task CreateIndexesAsync()
        {
            var initializer = new MongoIndexInitializer(
                _client,
                Options,
                RetryPolicy,
                NullLogger<MongoIndexInitializer>.Instance);

            await initializer.StartAsync(CancellationToken.None).ConfigureAwait(false);
        }

        public async Task<bool> IndexExistsAsync(string collectionName, string indexName)
        {
            var indexes = await _client.GetDatabase(_databaseName)
                .GetCollection<BsonDocument>(collectionName)
                .Indexes
                .ListAsync()
                .ConfigureAwait(false);
            var documents = await indexes.ToListAsync().ConfigureAwait(false);

            return documents.Any(index => index.TryGetValue("name", out var name) && name == indexName);
        }

        public async ValueTask DisposeAsync()
        {
            // Dropping the unique database keeps the local MongoDB container tidy after tests.
            await _client.DropDatabaseAsync(_databaseName).ConfigureAwait(false);
        }
    }

    private static OutboxMessage CreateOutboxMessage(Post post, Author author)
    {
        // Test outbox messages use the same factory as production so the persisted
        // contract shape remains covered by tests.
        return OutboxMessageFactory.PostCreated(
            new PostCreatedIntegrationEvent(post.Id.Value, author.Id.Value, post.Title, post.CreatedAt),
            DateTimeOffset.UtcNow);
    }
}
