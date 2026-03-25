using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Loggles.Core.Interfaces;
using Loggles.Core.Models;

namespace Loggles.Infrastructure.Queue;

public sealed class InMemoryIngestionQueue : IIngestionQueue
{
    private readonly Channel<LogEvent> _channel = Channel.CreateBounded<LogEvent>(
        new BoundedChannelOptions(50_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    public ValueTask EnqueueAsync(LogEvent evt, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(evt, ct);

    public async IAsyncEnumerable<LogEvent> ReadAllAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in _channel.Reader.ReadAllAsync(ct))
            yield return evt;
    }
}
