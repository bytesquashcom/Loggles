using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Loggles.Api.Controllers;

/// <summary>Stub OTLP metrics endpoint. Accepts and silently drops metrics — Loggles does not support metrics.</summary>
[ApiController]
[Authorize]
public sealed class MetricsController : ControllerBase
{
    [HttpPost("/v1/metrics")]
    public IActionResult Drop() => Ok();
}
