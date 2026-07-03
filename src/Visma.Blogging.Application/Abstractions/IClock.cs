namespace Visma.Blogging.Application.Abstractions;

/// <summary>
/// Provides time for application services.
/// </summary>
public interface IClock
{
    /// <summary>
    /// Gets the current UTC instant.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}
