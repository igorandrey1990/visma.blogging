namespace Visma.Blogging.Application.Blogging;

/// <summary>
/// Author information returned by the API.
/// </summary>
public sealed class AuthorResponse
{
    /// <summary>
    /// Creates an empty response instance for serializers.
    /// </summary>
    public AuthorResponse()
    {
    }

    /// <summary>
    /// Creates a populated author response.
    /// </summary>
    public AuthorResponse(Guid id, string name, string surname)
    {
        Id = id;
        Name = name;
        Surname = surname;
    }

    /// <summary>
    /// Author identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Author given name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Author family name.
    /// </summary>
    public string Surname { get; set; } = string.Empty;
}
