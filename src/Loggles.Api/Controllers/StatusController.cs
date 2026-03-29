using Loggles.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Loggles.Api.Controllers;

[ApiController]
[Authorize]
public sealed class StatusController : ControllerBase
{
    private readonly ILogStore _store;
    private readonly IMcpCallTracker _tracker;

    public StatusController(ILogStore store, IMcpCallTracker tracker)
    {
        _store = store;
        _tracker = tracker;
    }

    [HttpGet("/status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var tail = await _store.TailLogsAsync(1, ct: ct);
        var stats = await _store.GetStatsAsync(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow, ct: ct);
        var recent = _tracker.GetRecent(50);

        return Ok(new
        {
            totalLogs = stats.TotalCount,
            lastLogReceivedAt = tail.Count > 0 ? tail[0].Timestamp : (DateTime?)null,
            mcpCallCount = _tracker.TotalCalls,
            logsRetrievedByMcp = _tracker.TotalLogsRetrieved,
            recentCalls = recent.Select(c => new
            {
                toolName = c.ToolName,
                calledAt = c.CalledAt,
                durationMs = c.DurationMs,
                resultCount = c.ResultCount
            })
        });
    }
}
