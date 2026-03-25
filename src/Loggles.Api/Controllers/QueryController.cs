using Loggles.Core.DTOs;
using Loggles.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Loggles.Api.Controllers;

[ApiController]
[Authorize]
public sealed class QueryController : ControllerBase
{
    private readonly ILogStore _store;

    public QueryController(ILogStore store)
    {
        _store = store;
    }

    /// <summary>Search logs using a SearchQuery body.</summary>
    [HttpPost("/search")]
    public async Task<IActionResult> Search([FromBody] SearchQuery query, CancellationToken ct)
    {
        var result = await _store.SearchAsync(query, ct);
        return Ok(result);
    }

    /// <summary>Get a single log event by ID.</summary>
    [HttpGet("/logs/{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken ct)
    {
        var evt = await _store.GetByIdAsync(id, ct);
        return evt is null ? NotFound() : Ok(evt);
    }

    /// <summary>List all distinct property keys present across log events.</summary>
    [HttpGet("/meta/properties")]
    public async Task<IActionResult> GetProperties(CancellationToken ct)
    {
        var keys = await _store.GetPropertiesAsync(ct);
        return Ok(keys);
    }

    /// <summary>Get log count per level within a time range.</summary>
    [HttpGet("/stats/levels")]
    public async Task<IActionResult> GetLevelStats(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var f = from?.ToUniversalTime() ?? DateTime.UtcNow.AddDays(-1);
        var t = to?.ToUniversalTime() ?? DateTime.UtcNow;
        var counts = await _store.GetLevelCountsAsync(f, t, ct);
        return Ok(counts);
    }
}
