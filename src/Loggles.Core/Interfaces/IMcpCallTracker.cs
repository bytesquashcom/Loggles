using Loggles.Core.Models;

namespace Loggles.Core.Interfaces;

public interface IMcpCallTracker
{
    void Record(McpCallRecord call);
    IReadOnlyList<McpCallRecord> GetRecent(int count = 100);
    int TotalCalls { get; }
    long TotalLogsRetrieved { get; }
}
