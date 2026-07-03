namespace Visma.Blogging.Application.Messaging;

/// <summary>
/// Durable integration message waiting to be published by an infrastructure adapter.
/// </summary>
public sealed record OutboxMessage(
    Guid Id,
    string Type,
    int Version,
    DateTimeOffset OccurredAt,
    string PayloadJson);
