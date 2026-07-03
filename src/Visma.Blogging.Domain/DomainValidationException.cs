namespace Visma.Blogging.Domain;

/// <summary>
/// Represents a violated domain invariant.
/// </summary>
public sealed class DomainValidationException : Exception
{
    /// <summary>
    /// Creates a validation exception for the supplied field.
    /// </summary>
    public DomainValidationException(string field, string message)
        : base(message)
    {
        Field = field;
    }

    /// <summary>
    /// The logical field that failed validation.
    /// </summary>
    public string Field { get; }
}
