using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Visma.Blogging.Infrastructure.Logging;

/// <summary>
/// Persisted structured log entry.
/// </summary>
public sealed class MongoLogDocument
{
    /// <summary>
    /// MongoDB document identifier.
    /// </summary>
    [BsonId]
    public ObjectId Id { get; set; }

    /// <summary>
    /// UTC timestamp for the log event.
    /// </summary>
    public DateTime TimestampUtc { get; set; }

    /// <summary>
    /// Logger category, usually the fully qualified type name.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Log level such as Information, Warning, or Error.
    /// </summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>
    /// Event identifier value.
    /// </summary>
    public int EventId { get; set; }

    /// <summary>
    /// Event identifier name.
    /// </summary>
    public string? EventName { get; set; }

    /// <summary>
    /// Rendered log message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Distributed trace identifier when available.
    /// </summary>
    public string? TraceId { get; set; }

    /// <summary>
    /// Distributed span identifier when available.
    /// </summary>
    public string? SpanId { get; set; }

    /// <summary>
    /// Structured logging properties.
    /// </summary>
    public List<MongoLogPropertyDocument> Properties { get; set; } = [];

    /// <summary>
    /// Exception details when an exception was logged.
    /// </summary>
    public MongoExceptionDocument? Exception { get; set; }
}

/// <summary>
/// Persisted structured log property.
/// </summary>
public sealed class MongoLogPropertyDocument
{
    /// <summary>
    /// Property name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// String representation of the property value.
    /// </summary>
    public string? Value { get; set; }
}

/// <summary>
/// Persisted exception details.
/// </summary>
public sealed class MongoExceptionDocument
{
    /// <summary>
    /// Exception type name.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Exception message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Exception stack trace.
    /// </summary>
    public string? StackTrace { get; set; }
}
