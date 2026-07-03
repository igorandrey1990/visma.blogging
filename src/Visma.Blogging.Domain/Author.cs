namespace Visma.Blogging.Domain;

/// <summary>
/// Blog author aggregate member.
/// </summary>
public sealed class Author
{
    /// <summary>
    /// Maximum length for author name fields.
    /// </summary>
    public const int NameMaxLength = 100;

    private Author(AuthorId id, string name, string surname)
    {
        Id = id;
        Name = name;
        Surname = surname;
    }

    /// <summary>
    /// Unique author identifier.
    /// </summary>
    public AuthorId Id { get; }

    /// <summary>
    /// Author given name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Author family name.
    /// </summary>
    public string Surname { get; }

    /// <summary>
    /// Creates a validated author instance.
    /// </summary>
    public static Author Create(AuthorId id, string? name, string? surname)
    {
        return new Author(
            id,
            Guard.RequiredText(name, nameof(Name), NameMaxLength),
            Guard.RequiredText(surname, nameof(Surname), NameMaxLength));
    }
}
