namespace Visma.Blogging.Domain;

/// <summary>
/// Strongly typed author identifier.
/// </summary>
public readonly record struct AuthorId
{
    /// <summary>
    /// Creates an author identifier from a non-empty GUID.
    /// </summary>
    public AuthorId(Guid value)
    {
        Value = Guard.NonEmpty(value, nameof(AuthorId));
    }

    /// <summary>
    /// Raw GUID value.
    /// </summary>
    public Guid Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D");
}
