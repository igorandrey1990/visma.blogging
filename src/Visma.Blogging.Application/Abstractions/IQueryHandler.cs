namespace Visma.Blogging.Application.Abstractions;

/// <summary>
/// Handles a read-side query.
/// </summary>
public interface IQueryHandler<in TQuery, TResponse>
{
    /// <summary>
    /// Handles the query.
    /// </summary>
    Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
