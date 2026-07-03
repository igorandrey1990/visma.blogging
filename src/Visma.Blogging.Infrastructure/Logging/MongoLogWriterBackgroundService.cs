using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using Visma.Blogging.Infrastructure.Persistence;

namespace Visma.Blogging.Infrastructure.Logging;

/// <summary>
/// Background worker that drains queued log entries into MongoDB.
/// </summary>
public sealed class MongoLogWriterBackgroundService : BackgroundService
{
    private readonly IMongoCollection<MongoLogDocument> _logs;
    private readonly MongoLogQueue _queue;
    private readonly MongoRetryPolicy _retryPolicy;

    /// <summary>
    /// Creates the log writer.
    /// </summary>
    public MongoLogWriterBackgroundService(
        IMongoClient client,
        MongoBlogStoreOptions options,
        MongoLogQueue queue,
        MongoRetryPolicy retryPolicy)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        _queue = queue;
        _retryPolicy = retryPolicy;
        _logs = client.GetDatabase(options.DatabaseName).GetCollection<MongoLogDocument>(options.LogsCollectionName);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var document in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await _retryPolicy.ExecuteAsync(
                        token => _logs.InsertOneAsync(document, cancellationToken: token),
                        stoppingToken)
                    .ConfigureAwait(false);
            }
            catch when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Logging must never crash the application. If MongoDB is unavailable, drop the entry.
            }
        }
    }
}
