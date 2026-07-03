using Visma.Blogging.Application.Messaging;

namespace Visma.Blogging.Infrastructure.Messaging;

/// <summary>
/// Outbox message selected for publication.
/// </summary>
public sealed record OutboxMessageRecord(
    Guid Id,
    string Type,
    int Version,
    DateTimeOffset OccurredAt,
    string PayloadJson,
    int AttemptCount)
{
    /// <summary>
    /// Converts the persisted record back to the publishable application message.
    /// </summary>
    public OutboxMessage ToMessage()
    {
        return new OutboxMessage(Id, Type, Version, OccurredAt, PayloadJson);
    }
}
