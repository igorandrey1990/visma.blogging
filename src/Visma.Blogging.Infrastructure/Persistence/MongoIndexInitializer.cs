using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Visma.Blogging.Infrastructure.Logging;
using Visma.Blogging.Infrastructure.Messaging;

namespace Visma.Blogging.Infrastructure.Persistence;

/// <summary>
/// Creates operational MongoDB indexes used by reads, outbox publishing, trace lookup, and TTL cleanup.
/// </summary>
public sealed class MongoIndexInitializer : IHostedService
{
    private readonly IMongoClient _client;
    private readonly ILogger<MongoIndexInitializer> _logger;
    private readonly MongoBlogStoreOptions _options;
    private readonly MongoRetryPolicy _retryPolicy;

    /// <summary>
    /// Creates the initializer.
    /// </summary>
    public MongoIndexInitializer(
        IMongoClient client,
        MongoBlogStoreOptions options,
        MongoRetryPolicy retryPolicy,
        ILogger<MongoIndexInitializer> logger)
    {
        _client = client;
        _options = options;
        _retryPolicy = retryPolicy;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // MongoDB index creation is idempotent, so running this on every startup keeps environments reproducible.
        await _retryPolicy.ExecuteAsync(CreateIndexesAsync, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("MongoDB indexes are ready.");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task CreateIndexesAsync(CancellationToken cancellationToken)
    {
        var database = _client.GetDatabase(_options.DatabaseName);

        await CreateLogIndexesAsync(
                database.GetCollection<MongoLogDocument>(_options.LogsCollectionName),
                cancellationToken)
            .ConfigureAwait(false);

        await CreateIdempotencyIndexesAsync(
                database.GetCollection<BsonDocument>(_options.IdempotencyCollectionName),
                cancellationToken)
            .ConfigureAwait(false);

        await CreateOutboxIndexesAsync(
                database.GetCollection<MongoOutboxDocument>(_options.OutboxCollectionName),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task CreateLogIndexesAsync(
        IMongoCollection<MongoLogDocument> logs,
        CancellationToken cancellationToken)
    {
        var models = new[]
        {
            // TTL keeps application logs from growing forever in MongoDB.
            new CreateIndexModel<MongoLogDocument>(
                Builders<MongoLogDocument>.IndexKeys.Ascending(document => document.TimestampUtc),
                new CreateIndexOptions
                {
                    Name = "logs_timestamp_ttl",
                    ExpireAfter = TimeSpan.FromDays(Math.Max(1, _options.LogsRetentionDays))
                }),
            // Trace lookup index supports debugging a request across HTTP, logs, and background workers.
            new CreateIndexModel<MongoLogDocument>(
                Builders<MongoLogDocument>.IndexKeys.Ascending(document => document.TraceId),
                new CreateIndexOptions
                {
                    Name = "logs_trace_id"
                })
        };

        await logs.Indexes.CreateManyAsync(models, cancellationToken).ConfigureAwait(false);
    }

    private async Task CreateIdempotencyIndexesAsync(
        IMongoCollection<BsonDocument> idempotency,
        CancellationToken cancellationToken)
    {
        // Idempotency keys are only useful for a bounded retry window, so old records are expired automatically.
        var model = new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("CreatedAtUtc"),
            new CreateIndexOptions
            {
                Name = "idempotency_created_at_ttl",
                ExpireAfter = TimeSpan.FromHours(Math.Max(1, _options.IdempotencyRetentionHours))
            });

        await idempotency.Indexes.CreateOneAsync(model, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task CreateOutboxIndexesAsync(
        IMongoCollection<MongoOutboxDocument> outbox,
        CancellationToken cancellationToken)
    {
        var models = new[]
        {
            // The publisher worker repeatedly asks for messages that are ready to publish.
            // This compound index keeps that polling query efficient as the outbox grows.
            new CreateIndexModel<MongoOutboxDocument>(
                Builders<MongoOutboxDocument>.IndexKeys
                    .Ascending(document => document.Status)
                    .Ascending(document => document.NextAttemptAtUtc)
                    .Ascending(document => document.LockedUntilUtc),
                new CreateIndexOptions
                {
                    Name = "outbox_publishable"
                }),
            // Only successfully published messages expire. Pending or failed messages remain available for retry/debugging.
            new CreateIndexModel<MongoOutboxDocument>(
                Builders<MongoOutboxDocument>.IndexKeys.Ascending(document => document.PublishedAtUtc),
                new CreateIndexOptions<MongoOutboxDocument>
                {
                    Name = "outbox_published_at_ttl",
                    ExpireAfter = TimeSpan.FromDays(Math.Max(1, _options.PublishedOutboxRetentionDays)),
                    PartialFilterExpression = Builders<MongoOutboxDocument>.Filter.Eq(
                        document => document.Status,
                        MongoOutboxDocument.PublishedStatus)
                })
        };

        await outbox.Indexes.CreateManyAsync(models, cancellationToken).ConfigureAwait(false);
    }
}
