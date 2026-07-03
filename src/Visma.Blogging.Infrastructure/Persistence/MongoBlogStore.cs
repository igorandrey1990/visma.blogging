using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Visma.Blogging.Application.Blogging;
using Visma.Blogging.Application.Messaging;
using Visma.Blogging.Domain;
using Visma.Blogging.Infrastructure.Messaging;

namespace Visma.Blogging.Infrastructure.Persistence;

/// <summary>
/// MongoDB-backed blog store that persists one durable document per post.
/// </summary>
public sealed class MongoBlogStore : IPostCommandStore, IPostCreationStore, IPostQueryStore
{
    private readonly IMongoClient _client;
    private readonly IMongoCollection<MongoOutboxDocument> _outbox;
    private readonly IMongoCollection<PostDocument> _posts;
    private readonly MongoRetryPolicy _retryPolicy;

    /// <summary>
    /// Creates the MongoDB store.
    /// </summary>
    public MongoBlogStore(IMongoClient client, MongoBlogStoreOptions options, MongoRetryPolicy retryPolicy)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        _client = client;
        _retryPolicy = retryPolicy;
        var database = client.GetDatabase(options.DatabaseName);
        _posts = database.GetCollection<PostDocument>(options.PostsCollectionName);
        _outbox = database.GetCollection<MongoOutboxDocument>(options.OutboxCollectionName);
    }

    /// <inheritdoc />
    public async Task SaveAsync(Post post, Author author, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var document = PostDocument.From(post, author);

        try
        {
            await _retryPolicy.ExecuteAsync(
                    token => _posts.InsertOneAsync(document, cancellationToken: token),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new InvalidOperationException($"Post '{post.Id}' already exists.", exception);
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        Post post,
        Author author,
        OutboxMessage outboxMessage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var postDocument = PostDocument.From(post, author);
        var outboxDocument = MongoOutboxDocument.From(outboxMessage);

        try
        {
            await _retryPolicy.ExecuteAsync(
                    async token =>
                    {
                        using var session = await _client.StartSessionAsync(cancellationToken: token).ConfigureAwait(false);
                        await session.WithTransactionAsync(
                                async (sessionHandle, transactionToken) =>
                                {
                                    await _posts.InsertOneAsync(sessionHandle, postDocument, cancellationToken: transactionToken)
                                        .ConfigureAwait(false);
                                    await _outbox.InsertOneAsync(sessionHandle, outboxDocument, cancellationToken: transactionToken)
                                        .ConfigureAwait(false);

                                    return true;
                                },
                                cancellationToken: token)
                            .ConfigureAwait(false);
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new InvalidOperationException($"Post '{post.Id}' already exists.", exception);
        }
    }

    /// <inheritdoc />
    public async Task<PostDetails?> GetByIdAsync(PostId id, bool includeAuthor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var idValue = id.Value.ToString("D");
        var document = await _retryPolicy.ExecuteAsync(
                token => _posts.Find(post => post.Id == idValue).FirstOrDefaultAsync(token),
                cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            return null;
        }

        var post = document.ToPost();
        var author = includeAuthor ? document.Author.ToAuthor() : null;

        return new PostDetails(post, author);
    }

    private sealed class PostDocument
    {
        [BsonId]
        public string Id { get; set; } = string.Empty;

        public string AuthorId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; }

        public AuthorDocument Author { get; set; } = new();

        public List<BlogEventDocument> Events { get; set; } = [];

        public static PostDocument From(Post post, Author author)
        {
            var createdAtUtc = post.CreatedAt.UtcDateTime;

            return new PostDocument
            {
                Id = post.Id.Value.ToString("D"),
                AuthorId = post.AuthorId.Value.ToString("D"),
                Title = post.Title,
                Description = post.Description,
                Content = post.Content,
                CreatedAtUtc = createdAtUtc,
                Author = AuthorDocument.From(author),
                Events =
                [
                    BlogEventDocument.Create("author_registered", createdAtUtc),
                    BlogEventDocument.Create("post_published", createdAtUtc)
                ]
            };
        }

        public Post ToPost()
        {
            return Post.Create(
                new PostId(Guid.Parse(Id)),
                new AuthorId(Guid.Parse(AuthorId)),
                Title,
                Description,
                Content,
                new DateTimeOffset(DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Utc)));
        }
    }

    private sealed class AuthorDocument
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Surname { get; set; } = string.Empty;

        public static AuthorDocument From(Author author)
        {
            return new AuthorDocument
            {
                Id = author.Id.Value.ToString("D"),
                Name = author.Name,
                Surname = author.Surname
            };
        }

        public Author ToAuthor()
        {
            return Author.Create(new AuthorId(Guid.Parse(Id)), Name, Surname);
        }
    }

    private sealed class BlogEventDocument
    {
        public ObjectId Id { get; set; }

        public string Type { get; set; } = string.Empty;

        public DateTime OccurredAtUtc { get; set; }

        public static BlogEventDocument Create(string type, DateTime occurredAtUtc)
        {
            return new BlogEventDocument
            {
                Id = ObjectId.GenerateNewId(),
                Type = type,
                OccurredAtUtc = occurredAtUtc
            };
        }
    }
}
