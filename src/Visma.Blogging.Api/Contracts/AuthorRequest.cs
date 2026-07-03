namespace Visma.Blogging.Api;

/// <summary>
/// HTTP request payload for author data.
/// </summary>
public sealed class AuthorRequest
{
    /// <summary>
    /// Author given name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Author family name.
    /// </summary>
    public string? Surname { get; set; }
}
