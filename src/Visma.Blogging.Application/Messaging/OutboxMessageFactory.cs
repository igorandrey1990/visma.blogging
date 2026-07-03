using System.Text.Json;
using Visma.Blogging.Application.Blogging;

namespace Visma.Blogging.Application.Messaging;

/// <summary>
/// Creates integration messages from application events.
/// </summary>
public static class OutboxMessageFactory
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Creates the public post-created message contract.
    /// </summary>
    public static OutboxMessage PostCreated(
        PostCreatedIntegrationEvent integrationEvent,
        DateTimeOffset occurredAt)
    {
        return new OutboxMessage(
            Guid.NewGuid(),
            "post.created.v1",
            1,
            occurredAt,
            JsonSerializer.Serialize(integrationEvent, SerializerOptions));
    }
}
