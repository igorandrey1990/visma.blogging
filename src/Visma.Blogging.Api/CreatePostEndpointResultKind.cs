namespace Visma.Blogging.Api;

/// <summary>
/// Possible API-level outcomes when creating a post.
/// </summary>
public enum CreatePostEndpointResultKind
{
    Created,
    Replayed,
    ApplicationFailure,
    BadRequest,
    Conflict
}
