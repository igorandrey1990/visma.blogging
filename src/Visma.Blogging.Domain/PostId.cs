namespace Visma.Blogging.Domain;

/// <summary>
/// Strongly typed post identifier.
/// </summary>
public readonly record struct PostId
{
    /// <summary>
    /// Creates a post identifier from a non-empty GUID.
    /// </summary>
    public PostId(Guid value)
    {
        Value = Guard.NonEmpty(value, nameof(PostId));
    }

    /// <summary>
    /// Raw GUID value.
    /// </summary>
    public Guid Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D");
}
