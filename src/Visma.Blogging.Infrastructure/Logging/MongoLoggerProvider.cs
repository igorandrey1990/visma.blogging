using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace Visma.Blogging.Infrastructure.Logging;

/// <summary>
/// Logging provider that queues log entries for MongoDB persistence.
/// </summary>
public sealed class MongoLoggerProvider : ILoggerProvider
{
    private readonly MongoLogQueue _queue;

    /// <summary>
    /// Creates the provider.
    /// </summary>
    public MongoLoggerProvider(MongoLogQueue queue)
    {
        _queue = queue;
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        return new MongoLogger(categoryName, _queue);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class MongoLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly MongoLogQueue _queue;

        public MongoLogger(string categoryName, MongoLogQueue queue)
        {
            _categoryName = categoryName;
            _queue = queue;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel is not LogLevel.None && !_categoryName.StartsWith("MongoDB.", StringComparison.Ordinal);
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var activity = Activity.Current;
            var document = new MongoLogDocument
            {
                Id = ObjectId.GenerateNewId(),
                TimestampUtc = DateTime.UtcNow,
                Category = _categoryName,
                Level = logLevel.ToString(),
                EventId = eventId.Id,
                EventName = eventId.Name,
                Message = formatter(state, exception),
                TraceId = activity?.TraceId.ToString(),
                SpanId = activity?.SpanId.ToString(),
                Properties = GetProperties(state),
                Exception = exception is null
                    ? null
                    : new MongoExceptionDocument
                    {
                        Type = exception.GetType().FullName ?? exception.GetType().Name,
                        Message = exception.Message,
                        StackTrace = exception.StackTrace
                    }
            };

            _queue.Enqueue(document);
        }

        private static List<MongoLogPropertyDocument> GetProperties<TState>(TState state)
        {
            if (state is not IEnumerable<KeyValuePair<string, object?>> properties)
            {
                return [];
            }

            return properties
                .Where(property => property.Key != "{OriginalFormat}")
                .Select(property => new MongoLogPropertyDocument
                {
                    Name = property.Key,
                    Value = property.Value?.ToString()
                })
                .ToList();
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
