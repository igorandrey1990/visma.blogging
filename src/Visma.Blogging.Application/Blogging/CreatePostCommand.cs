namespace Visma.Blogging.Application.Blogging;

/// <summary>
/// Command for publishing a post.
/// </summary>
public sealed record CreatePostCommand(string? Title, string? Description, string? Content, CreateAuthorCommand? Author);
