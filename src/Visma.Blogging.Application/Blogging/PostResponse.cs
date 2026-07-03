using Visma.Blogging.Domain;

namespace Visma.Blogging.Application.Blogging;

/// <summary>
/// Post representation returned by use cases.
/// </summary>
public sealed class PostResponse
{
    /// <summary>
    /// Creates an empty response instance for serializers.
    /// </summary>
    public PostResponse()
    {
    }

    /// <summary>
    /// Creates a populated post response.
    /// </summary>
    public PostResponse(
        Guid id,
        Guid authorId,
        string title,
        string description,
        string content,
        DateTimeOffset createdAt,
        AuthorResponse? author)
    {
        Id = id;
        AuthorId = authorId;
        Title = title;
        Description = description;
        Content = content;
        CreatedAt = createdAt;
        Author = author;
    }

    /// <summary>
    /// Post identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Author identifier.
    /// </summary>
    public Guid AuthorId { get; set; }

    /// <summary>
    /// Post title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Short post description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Full post content.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// UTC creation instant.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Optional author information.
    /// </summary>
    public AuthorResponse? Author { get; set; }

    /// <summary>
    /// Maps domain state to the response contract.
    /// </summary>
    public static PostResponse From(Post post, Author? author)
    {
        return new PostResponse(
            post.Id.Value,
            post.AuthorId.Value,
            post.Title,
            post.Description,
            post.Content,
            post.CreatedAt,
            author is null ? null : new AuthorResponse(author.Id.Value, author.Name, author.Surname));
    }
}
