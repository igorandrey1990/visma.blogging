namespace Visma.Blogging.Infrastructure.Persistence;

/// <summary>
/// MongoDB persistence settings for the blogging store.
/// </summary>
public sealed class MongoBlogStoreOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Mongo";

    /// <summary>
    /// MongoDB connection string.
    /// </summary>
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";

    /// <summary>
    /// Database name used by the API.
    /// </summary>
    public string DatabaseName { get; set; } = "visma_blogging";

    /// <summary>
    /// Collection name for persisted post documents.
    /// </summary>
    public string PostsCollectionName { get; set; } = "posts";

    /// <summary>
    /// Collection name for persisted application log documents.
    /// </summary>
    public string LogsCollectionName { get; set; } = "logs";

    /// <summary>
    /// Collection name for create-post idempotency documents.
    /// </summary>
    public string IdempotencyCollectionName { get; set; } = "idempotency";

    /// <summary>
    /// Collection name for outbox messages waiting to be published.
    /// </summary>
    public string OutboxCollectionName { get; set; } = "outbox";

    /// <summary>
    /// Maximum attempts for transient MongoDB operations.
    /// </summary>
    public int RetryMaxAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay in milliseconds between transient MongoDB retry attempts.
    /// </summary>
    public int RetryBaseDelayMilliseconds { get; set; } = 100;

    /// <summary>
    /// Number of days to keep structured API logs.
    /// </summary>
    public int LogsRetentionDays { get; set; } = 7;

    /// <summary>
    /// Number of hours to keep create-post idempotency records.
    /// </summary>
    public int IdempotencyRetentionHours { get; set; } = 24;

    /// <summary>
    /// Number of days to keep published outbox messages.
    /// </summary>
    public int PublishedOutboxRetentionDays { get; set; } = 7;
}
