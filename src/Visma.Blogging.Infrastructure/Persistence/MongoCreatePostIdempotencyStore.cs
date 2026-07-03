using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Visma.Blogging.Application.Blogging;

namespace Visma.Blogging.Infrastructure.Persistence;

/// <summary>
/// MongoDB-backed idempotency store for the create-post endpoint.
/// </summary>
public sealed class MongoCreatePostIdempotencyStore : ICreatePostIdempotencyStore
{
    private const string CompletedStatus = "completed";
    private const string ProcessingStatus = "processing";
    private readonly IMongoCollection<CreatePostIdempotencyDocument> _collection;
    private readonly MongoRetryPolicy _retryPolicy;

    /// <summary>
    /// Creates the store.
    /// </summary>
    public MongoCreatePostIdempotencyStore(
        IMongoClient client,
        MongoBlogStoreOptions options,
        MongoRetryPolicy retryPolicy)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        _retryPolicy = retryPolicy;
        _collection = client.GetDatabase(options.DatabaseName)
            .GetCollection<CreatePostIdempotencyDocument>(options.IdempotencyCollectionName);
    }

    /// <inheritdoc />
    public async Task<CreatePostIdempotencyStartResult> TryStartAsync(
        string key,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var document = CreatePostIdempotencyDocument.Started(key, requestHash);

        try
        {
            await _retryPolicy.ExecuteAsync(
                    token => _collection.InsertOneAsync(document, cancellationToken: token),
                    cancellationToken)
                .ConfigureAwait(false);

            return CreatePostIdempotencyStartResult.Started();
        }
        catch (MongoWriteException exception) when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return await ReadExistingAsync(key, requestHash, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task CompleteAsync(
        string key,
        string requestHash,
        PostResponse response,
        string location,
        CancellationToken cancellationToken)
    {
        var filter = Builders<CreatePostIdempotencyDocument>.Filter.And(
            Builders<CreatePostIdempotencyDocument>.Filter.Eq(document => document.Id, key),
            Builders<CreatePostIdempotencyDocument>.Filter.Eq(document => document.RequestHash, requestHash),
            Builders<CreatePostIdempotencyDocument>.Filter.Eq(document => document.Status, ProcessingStatus));

        var update = Builders<CreatePostIdempotencyDocument>.Update
            .Set(document => document.Status, CompletedStatus)
            .Set(document => document.CompletedAtUtc, DateTime.UtcNow)
            .Set(document => document.Location, location)
            .Set(document => document.Response, PostResponseDocument.From(response));

        await _retryPolicy.ExecuteAsync(
                token => _collection.UpdateOneAsync(filter, update, cancellationToken: token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, string requestHash, CancellationToken cancellationToken)
    {
        var filter = Builders<CreatePostIdempotencyDocument>.Filter.And(
            Builders<CreatePostIdempotencyDocument>.Filter.Eq(document => document.Id, key),
            Builders<CreatePostIdempotencyDocument>.Filter.Eq(document => document.RequestHash, requestHash),
            Builders<CreatePostIdempotencyDocument>.Filter.Eq(document => document.Status, ProcessingStatus));

        await _retryPolicy.ExecuteAsync(
                token => _collection.DeleteOneAsync(filter, token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<CreatePostIdempotencyStartResult> ReadExistingAsync(
        string key,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var existing = await _retryPolicy.ExecuteAsync(
                token => _collection.Find(document => document.Id == key).FirstOrDefaultAsync(token),
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null || existing.RequestHash != requestHash)
        {
            return CreatePostIdempotencyStartResult.RequestMismatch();
        }

        if (existing.Status == CompletedStatus && existing.Response is not null && existing.Location is not null)
        {
            return CreatePostIdempotencyStartResult.Completed(existing.Response.ToResponse(), existing.Location);
        }

        return CreatePostIdempotencyStartResult.InProgress();
    }

    private sealed class CreatePostIdempotencyDocument
    {
        [BsonId]
        public string Id { get; set; } = string.Empty;

        public string RequestHash { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? CompletedAtUtc { get; set; }

        public string? Location { get; set; }

        public PostResponseDocument? Response { get; set; }

        public static CreatePostIdempotencyDocument Started(string key, string requestHash)
        {
            return new CreatePostIdempotencyDocument
            {
                Id = key,
                RequestHash = requestHash,
                Status = ProcessingStatus,
                CreatedAtUtc = DateTime.UtcNow
            };
        }
    }

    private sealed class PostResponseDocument
    {
        public string Id { get; set; } = string.Empty;

        public string AuthorId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; }

        public AuthorResponseDocument? Author { get; set; }

        public static PostResponseDocument From(PostResponse response)
        {
            return new PostResponseDocument
            {
                Id = response.Id.ToString("D"),
                AuthorId = response.AuthorId.ToString("D"),
                Title = response.Title,
                Description = response.Description,
                Content = response.Content,
                CreatedAtUtc = response.CreatedAt.UtcDateTime,
                Author = response.Author is null ? null : AuthorResponseDocument.From(response.Author)
            };
        }

        public PostResponse ToResponse()
        {
            return new PostResponse(
                Guid.Parse(Id),
                Guid.Parse(AuthorId),
                Title,
                Description,
                Content,
                new DateTimeOffset(DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Utc)),
                Author?.ToResponse());
        }
    }

    private sealed class AuthorResponseDocument
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Surname { get; set; } = string.Empty;

        public static AuthorResponseDocument From(AuthorResponse response)
        {
            return new AuthorResponseDocument
            {
                Id = response.Id.ToString("D"),
                Name = response.Name,
                Surname = response.Surname
            };
        }

        public AuthorResponse ToResponse()
        {
            return new AuthorResponse(Guid.Parse(Id), Name, Surname);
        }
    }
}
