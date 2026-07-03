namespace Visma.Blogging.Application.Blogging;

/// <summary>
/// Message contract published when a post is created.
/// </summary>
public sealed record PostCreatedIntegrationEvent(
    Guid PostId,
    Guid AuthorId,
    string Title,
    DateTimeOffset CreatedAt);
