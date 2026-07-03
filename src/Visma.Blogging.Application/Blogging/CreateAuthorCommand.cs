namespace Visma.Blogging.Application.Blogging;

/// <summary>
/// Author payload for creating a post.
/// </summary>
public sealed record CreateAuthorCommand(string? Name, string? Surname);
