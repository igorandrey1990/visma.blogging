using MongoDB.Driver;
using Visma.Blogging.Application.Messaging;
using Visma.Blogging.Infrastructure.Persistence;

namespace Visma.Blogging.Infrastructure.Messaging;

/// <summary>
/// MongoDB-backed outbox used to persist messages before RabbitMQ publication.
/// </summary>
public sealed class MongoOutboxStore : IOutboxWriter, IOutboxReader
{
    private readonly IMongoCollection<MongoOutboxDocument> _outbox;
    private readonly MongoRetryPolicy _retryPolicy;

    /// <summary>
    /// Creates the store.
    /// </summary>
    public MongoOutboxStore(
        IMongoClient client,
        MongoBlogStoreOptions options,
        MongoRetryPolicy retryPolicy)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        _retryPolicy = retryPolicy;
        _outbox = client.GetDatabase(options.DatabaseName).GetCollection<MongoOutboxDocument>(options.OutboxCollectionName);
    }

    /// <inheritdoc />
    public async Task SaveAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var document = MongoOutboxDocument.From(message);

        await _retryPolicy.ExecuteAsync(
                token => _outbox.InsertOneAsync(document, cancellationToken: token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<OutboxMessageRecord>> ClaimPendingAsync(
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var available = Builders<MongoOutboxDocument>.Filter.And(
            Builders<MongoOutboxDocument>.Filter.In(document => document.Status, [MongoOutboxDocument.PendingStatus, MongoOutboxDocument.FailedStatus]),
            Builders<MongoOutboxDocument>.Filter.Lte(document => document.NextAttemptAtUtc, now),
            Builders<MongoOutboxDocument>.Filter.Or(
                Builders<MongoOutboxDocument>.Filter.Eq(document => document.LockedUntilUtc, null),
                Builders<MongoOutboxDocument>.Filter.Lte(document => document.LockedUntilUtc, now)));

        var candidates = await _retryPolicy.ExecuteAsync(
                token => _outbox.Find(available)
                    .SortBy(document => document.OccurredAtUtc)
                    .Limit(batchSize)
                    .ToListAsync(token),
                cancellationToken)
            .ConfigureAwait(false);

        var claimed = new List<OutboxMessageRecord>();
        foreach (var candidate in candidates)
        {
            var claimFilter = Builders<MongoOutboxDocument>.Filter.And(
                Builders<MongoOutboxDocument>.Filter.Eq(document => document.Id, candidate.Id),
                available);
            var claimUpdate = Builders<MongoOutboxDocument>.Update
                .Set(document => document.Status, MongoOutboxDocument.PublishingStatus)
                .Set(document => document.LockedUntilUtc, now.Add(lockDuration));

            var result = await _retryPolicy.ExecuteAsync(
                    token => _outbox.UpdateOneAsync(claimFilter, claimUpdate, cancellationToken: token),
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.ModifiedCount == 1)
            {
                claimed.Add(candidate.ToRecord());
            }
        }

        return claimed;
    }

    /// <inheritdoc />
    public async Task MarkPublishedAsync(Guid id, CancellationToken cancellationToken)
    {
        var filter = Builders<MongoOutboxDocument>.Filter.Eq(document => document.Id, id.ToString("D"));
        var update = Builders<MongoOutboxDocument>.Update
            .Set(document => document.Status, MongoOutboxDocument.PublishedStatus)
            .Set(document => document.PublishedAtUtc, DateTime.UtcNow)
            .Set(document => document.LockedUntilUtc, null);

        await _retryPolicy.ExecuteAsync(
                token => _outbox.UpdateOneAsync(filter, update, cancellationToken: token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken)
    {
        var filter = Builders<MongoOutboxDocument>.Filter.Eq(document => document.Id, id.ToString("D"));
        var existing = await _retryPolicy.ExecuteAsync(
                token => _outbox.Find(filter).FirstOrDefaultAsync(token),
                cancellationToken)
            .ConfigureAwait(false);

        var attempts = existing?.AttemptCount + 1 ?? 1;
        var delaySeconds = Math.Min(60, Math.Pow(2, attempts));
        var update = Builders<MongoOutboxDocument>.Update
            .Set(document => document.Status, MongoOutboxDocument.FailedStatus)
            .Set(document => document.LastError, error)
            .Set(document => document.NextAttemptAtUtc, DateTime.UtcNow.AddSeconds(delaySeconds))
            .Set(document => document.LockedUntilUtc, null)
            .Set(document => document.AttemptCount, attempts);

        await _retryPolicy.ExecuteAsync(
                token => _outbox.UpdateOneAsync(filter, update, cancellationToken: token),
                cancellationToken)
            .ConfigureAwait(false);
    }

}
