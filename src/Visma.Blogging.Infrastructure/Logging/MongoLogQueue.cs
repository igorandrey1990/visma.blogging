using System.Threading.Channels;

namespace Visma.Blogging.Infrastructure.Logging;

/// <summary>
/// Non-blocking queue used by the MongoDB logger provider.
/// </summary>
public sealed class MongoLogQueue
{
    private readonly Channel<MongoLogDocument> _channel = Channel.CreateBounded<MongoLogDocument>(
        new BoundedChannelOptions(capacity: 10_000)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

    /// <summary>
    /// Reads queued log documents.
    /// </summary>
    public ChannelReader<MongoLogDocument> Reader => _channel.Reader;

    /// <summary>
    /// Queues a log document if capacity is available.
    /// </summary>
    public void Enqueue(MongoLogDocument document)
    {
        _channel.Writer.TryWrite(document);
    }
}
