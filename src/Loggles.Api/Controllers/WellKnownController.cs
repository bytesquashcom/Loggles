using Microsoft.AspNetCore.Mvc;

namespace Loggles.Api.Controllers;

/// <summary>
/// Provides OAuth 2.0 authorization server metadata (RFC 8414) for MCP client discovery.
/// This is a minimal no-auth implementation for local development.
/// </summary>
[ApiController]
public sealed class WellKnownController : ControllerBase
{
    private readonly ILogger<WellKnownController> _logger;

    public WellKnownController(ILogger<WellKnownController> logger)
    {
        _logger = logger;
    }

    [HttpGet("/.well-known/oauth-protected-resource")]
    public IActionResult GetProtectedResourceMetadata()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        _logger.LogInformation("OAuth: protected-resource metadata request from {RemoteIp}; base_url={BaseUrl}",
            HttpContext.Connection.RemoteIpAddress, baseUrl);
        return Ok(new
        {
            resource = baseUrl,
            authorization_servers = new[] { baseUrl }
        });
    }

    [HttpGet("/.well-known/oauth-authorization-server")]
    public IActionResult GetAuthServerMetadata()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        _logger.LogInformation("OAuth: discovery request from {RemoteIp}; base_url={BaseUrl}",
            HttpContext.Connection.RemoteIpAddress, baseUrl);
        return Ok(new
        {
            issuer = baseUrl,
            authorization_endpoint = $"{baseUrl}/oauth/authorize",
            token_endpoint = $"{baseUrl}/oauth/token",
            response_types_supported = new[] { "code" },
            grant_types_supported = new[] { "authorization_code", "client_credentials" },
            token_endpoint_auth_methods_supported = new[] { "none" },
            code_challenge_methods_supported = new[] { "S256" },
            scopes_supported = new[] { "mcp" },
            registration_endpoint = $"{baseUrl}/oauth/register"
        });
    }
}
