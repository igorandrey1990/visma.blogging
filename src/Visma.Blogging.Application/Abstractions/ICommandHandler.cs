namespace Visma.Blogging.Application.Abstractions;

/// <summary>
/// Handles a write-side command.
/// </summary>
public interface ICommandHandler<in TCommand, TResponse>
{
    /// <summary>
    /// Handles the command.
    /// </summary>
    Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
