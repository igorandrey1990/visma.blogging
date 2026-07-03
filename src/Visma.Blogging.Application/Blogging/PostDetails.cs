using Visma.Blogging.Domain;

namespace Visma.Blogging.Application.Blogging;

/// <summary>
/// Read model used by query handlers.
/// </summary>
public sealed record PostDetails(Post Post, Author? Author);
