using MongoDB.Bson.Serialization.Attributes;
using Visma.Blogging.Application.Messaging;

namespace Visma.Blogging.Infrastructure.Messaging;

internal sealed class MongoOutboxDocument
{
    public const string FailedStatus = "failed";
    public const string PendingStatus = "pending";
    public const string PublishedStatus = "published";
    public const string PublishingStatus = "publishing";

    [BsonId]
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public int Version { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public string PayloadJson { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public int AttemptCount { get; set; }

    public DateTime NextAttemptAtUtc { get; set; }

    public DateTime? LockedUntilUtc { get; set; }

    public DateTime? PublishedAtUtc { get; set; }

    public string? LastError { get; set; }

    public static MongoOutboxDocument From(OutboxMessage message)
    {
        var occurredAtUtc = message.OccurredAt.UtcDateTime;

        return new MongoOutboxDocument
        {
            Id = message.Id.ToString("D"),
            Type = message.Type,
            Version = message.Version,
            OccurredAtUtc = occurredAtUtc,
            PayloadJson = message.PayloadJson,
            Status = PendingStatus,
            NextAttemptAtUtc = occurredAtUtc
        };
    }

    public OutboxMessageRecord ToRecord()
    {
        return new OutboxMessageRecord(
            Guid.Parse(Id),
            Type,
            Version,
            new DateTimeOffset(DateTime.SpecifyKind(OccurredAtUtc, DateTimeKind.Utc)),
            PayloadJson,
            AttemptCount);
    }
}
