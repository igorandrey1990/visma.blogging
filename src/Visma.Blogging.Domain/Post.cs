namespace Visma.Blogging.Domain;

/// <summary>
/// Blog post aggregate root.
/// </summary>
public sealed class Post
{
    /// <summary>
    /// Maximum title length.
    /// </summary>
    public const int TitleMaxLength = 200;

    /// <summary>
    /// Maximum description length.
    /// </summary>
    public const int DescriptionMaxLength = 500;

    /// <summary>
    /// Maximum body content length.
    /// </summary>
    public const int ContentMaxLength = 20_000;

    private Post(
        PostId id,
        AuthorId authorId,
        string title,
        string description,
        string content,
        DateTimeOffset createdAt)
    {
        Id = id;
        AuthorId = authorId;
        Title = title;
        Description = description;
        Content = content;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Unique post identifier.
    /// </summary>
    public PostId Id { get; }

    /// <summary>
    /// Identifier of the post author.
    /// </summary>
    public AuthorId AuthorId { get; }

    /// <summary>
    /// Post title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Short post description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Full post content.
    /// </summary>
    public string Content { get; }

    /// <summary>
    /// UTC creation instant.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Creates a validated post.
    /// </summary>
    public static Post Create(
        PostId id,
        AuthorId authorId,
        string? title,
        string? description,
        string? content,
        DateTimeOffset createdAt)
    {
        // Creation time is part of the post's identity/history, so avoid accepting an implicit default.
        if (createdAt == default)
        {
            throw new DomainValidationException(nameof(CreatedAt), "CreatedAt must be specified.");
        }

        // Normalize the timestamp once at the domain boundary.
        return new Post(
            id,
            authorId,
            Guard.RequiredText(title, nameof(Title), TitleMaxLength),
            Guard.RequiredText(description, nameof(Description), DescriptionMaxLength),
            Guard.RequiredText(content, nameof(Content), ContentMaxLength),
            createdAt.ToUniversalTime());
    }
}
