using System.Text.Json.Nodes;
using Loggles.Core.Interfaces;
using Loggles.Core.Models;
using Loggles.Core.Protos.Collector.Logs.V1;
using Loggles.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Loggles.Api.Controllers;

[ApiController]
[Authorize]
public sealed class IngestController : ControllerBase
{
    private readonly IIngestionQueue _queue;

    public IngestController(IIngestionQueue queue)
    {
        _queue = queue;
    }

    /// <summary>OTLP/HTTP ingestion (POST /v1/logs). Accepts application/json and application/x-protobuf.</summary>
    [HttpPost("/v1/logs")]
    public async Task<IActionResult> OtlpIngest(CancellationToken ct)
    {
        IReadOnlyList<LogEvent> events;

        var contentType = Request.ContentType ?? string.Empty;
        if (contentType.StartsWith("application/x-protobuf", StringComparison.OrdinalIgnoreCase))
        {
            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms, ct);
            var request = ExportLogsServiceRequest.Parser.ParseFrom(ms.ToArray());
            events = OtlpProtoMapper.Map(request);
        }
        else
        {
            var doc = await JsonNode.ParseAsync(Request.Body, cancellationToken: ct);
            if (doc is null) return BadRequest("Empty body");
            events = OtlpMapper.Map(doc);
        }

        foreach (var evt in events)
            await _queue.EnqueueAsync(evt, ct);

        return Accepted();
    }
}
