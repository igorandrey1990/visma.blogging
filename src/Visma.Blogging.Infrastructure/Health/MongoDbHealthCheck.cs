using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;
using Visma.Blogging.Infrastructure.Persistence;

namespace Visma.Blogging.Infrastructure.Health;

/// <summary>
/// Health check that verifies the configured MongoDB database responds to a ping command.
/// </summary>
public sealed class MongoDbHealthCheck : IHealthCheck
{
    private readonly IMongoClient _client;
    private readonly MongoBlogStoreOptions _options;

    /// <summary>
    /// Creates the health check.
    /// </summary>
    public MongoDbHealthCheck(IMongoClient client, MongoBlogStoreOptions options)
    {
        _client = client;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var database = _client.GetDatabase(_options.DatabaseName);
            // ping is intentionally cheap: readiness should prove connectivity without doing business work.
            await database.RunCommandAsync<BsonDocument>(
                    new BsonDocument("ping", 1),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return HealthCheckResult.Healthy("MongoDB responded to ping.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("MongoDB did not respond to ping.", exception);
        }
    }
}
