using Visma.Blogging.Application.Abstractions;

namespace Visma.Blogging.Infrastructure;

/// <summary>
/// GUID-based identifier generator.
/// </summary>
public sealed class GuidIdGenerator : IIdGenerator
{
    /// <inheritdoc />
    public Guid NewId() => Guid.NewGuid();
}
