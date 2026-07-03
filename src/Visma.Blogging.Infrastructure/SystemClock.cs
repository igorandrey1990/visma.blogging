using Visma.Blogging.Application.Abstractions;

namespace Visma.Blogging.Infrastructure;

/// <summary>
/// System UTC clock.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
