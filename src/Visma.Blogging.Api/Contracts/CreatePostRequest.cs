namespace Visma.Blogging.Api;

/// <summary>
/// HTTP request payload for creating a post.
/// </summary>
public sealed class CreatePostRequest
{
    /// <summary>
    /// Post title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Short post description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Full post content.
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Author details supplied with the post.
    /// </summary>
    public AuthorRequest? Author { get; set; }
}
