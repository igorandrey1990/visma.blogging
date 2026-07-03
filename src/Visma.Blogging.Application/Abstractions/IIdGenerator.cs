namespace Visma.Blogging.Application.Abstractions;

/// <summary>
/// Generates identifiers for new aggregates.
/// </summary>
public interface IIdGenerator
{
    /// <summary>
    /// Creates a new identifier.
    /// </summary>
    Guid NewId();
}
