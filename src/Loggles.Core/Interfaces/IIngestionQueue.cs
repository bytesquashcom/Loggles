using Loggles.Core.Models;

namespace Loggles.Core.Interfaces;

public interface IIngestionQueue
{
    ValueTask EnqueueAsync(LogEvent evt, CancellationToken ct = default);
    IAsyncEnumerable<LogEvent> ReadAllAsync(CancellationToken ct = default);
}
