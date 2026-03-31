using System.Collections.Concurrent;
using Loggles.Core.Interfaces;
using Loggles.Core.Models;

namespace Loggles.Infrastructure.Tracking;

public sealed class InMemoryMcpCallTracker : IMcpCallTracker
{
    private const int MaxEntries = 500;

    private readonly ConcurrentQueue<McpCallRecord> _queue = new();
    private int _totalCalls;
    private long _totalLogsRetrieved;

    public int TotalCalls => _totalCalls;
    public long TotalLogsRetrieved => _totalLogsRetrieved;

    public void Record(McpCallRecord call)
    {
        _queue.Enqueue(call);
        Interlocked.Increment(ref _totalCalls);
        if (call.ResultCount.HasValue)
            Interlocked.Add(ref _totalLogsRetrieved, call.ResultCount.Value);

        // Trim to MaxEntries
        while (_queue.Count > MaxEntries)
            _queue.TryDequeue(out _);
    }

    public IReadOnlyList<McpCallRecord> GetRecent(int count = 100)
    {
        var all = _queue.ToArray();
        return all.Length <= count
            ? [.. all.Reverse()]
            : [.. all[^count..].Reverse()];
    }
}
