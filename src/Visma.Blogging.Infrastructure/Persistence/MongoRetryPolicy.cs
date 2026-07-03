using MongoDB.Driver;

namespace Visma.Blogging.Infrastructure.Persistence;

/// <summary>
/// Small retry policy for transient MongoDB operations.
/// </summary>
public sealed class MongoRetryPolicy
{
    private readonly TimeSpan _baseDelay;
    private readonly int _maxAttempts;

    /// <summary>
    /// Creates the policy from MongoDB persistence options.
    /// </summary>
    public MongoRetryPolicy(MongoBlogStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _maxAttempts = Math.Max(1, options.RetryMaxAttempts);
        _baseDelay = TimeSpan.FromMilliseconds(Math.Max(0, options.RetryBaseDelayMilliseconds));
    }

    /// <summary>
    /// Executes an operation with retry for transient failures.
    /// </summary>
    public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        await ExecuteAsync<object?>(
                async token =>
                {
                    await operation(token).ConfigureAwait(false);
                    return null;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Executes an operation with retry for transient failures.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (ShouldRetry(exception, attempt))
            {
                var delay = TimeSpan.FromMilliseconds(_baseDelay.TotalMilliseconds * attempt);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private bool ShouldRetry(Exception exception, int attempt)
    {
        return attempt < _maxAttempts && IsTransient(exception);
    }

    private static bool IsTransient(Exception exception)
    {
        return exception switch
        {
            TimeoutException => true,
            MongoConnectionException => true,
            MongoExecutionTimeoutException => true,
            MongoException mongoException when mongoException.HasErrorLabel("RetryableWriteError") => true,
            MongoException mongoException when mongoException.HasErrorLabel("TransientTransactionError") => true,
            _ => false
        };
    }
}
