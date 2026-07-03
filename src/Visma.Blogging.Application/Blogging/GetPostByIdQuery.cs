namespace Visma.Blogging.Application.Blogging;

/// <summary>
/// Query for fetching one post.
/// </summary>
public sealed record GetPostByIdQuery(Guid Id, bool IncludeAuthor);
