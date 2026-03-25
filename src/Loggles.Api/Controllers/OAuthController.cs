using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Loggles.Api.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Loggles.Api.Controllers;

/// <summary>
/// Minimal OAuth 2.0 authorization server for local development (RFC 6749, RFC 7591, RFC 7636).
/// Auto-approves all requests and issues the configured API key as the access token.
/// </summary>
[ApiController]
[Route("oauth")]
public sealed class OAuthController : ControllerBase
{
    private readonly ILogger<OAuthController> _logger;
    private readonly AuthOptions _authOptions;

    public OAuthController(ILogger<OAuthController> logger, IOptions<AuthOptions> authOptions)
    {
        _logger = logger;
        _authOptions = authOptions.Value;
    }

    // In-memory store: code -> (codeChallenge, redirectUri)
    private static readonly ConcurrentDictionary<string, (string CodeChallenge, string RedirectUri)> _codes = new();

    /// <summary>RFC 7591 — Dynamic Client Registration.</summary>
    [HttpPost("register")]
    public IActionResult Register([FromBody] ClientRegistrationRequest? request)
    {
        _logger.LogInformation("OAuth: client registration request; redirect_uris={RedirectUris}",
            string.Join(", ", request?.RedirectUris ?? []));
        return StatusCode(201, new
        {
            client_id = "loggles-mcp-client",
            client_id_issued_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            redirect_uris = request?.RedirectUris ?? [],
            grant_types = new[] { "authorization_code" },
            response_types = new[] { "code" },
            token_endpoint_auth_method = "none"
        });
    }

    /// <summary>RFC 6749 §4.1.2 — Authorization endpoint. Auto-approves and redirects with code.</summary>
    [HttpGet("authorize")]
    public IActionResult Authorize(
        [FromQuery(Name = "redirect_uri")] string redirectUri,
        [FromQuery(Name = "code_challenge")] string codeChallenge,
        [FromQuery(Name = "state")] string? state)
    {
        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        _codes[code] = (codeChallenge, redirectUri);

        var location = $"{redirectUri}?code={Uri.EscapeDataString(code)}"
                       + (state is not null ? $"&state={Uri.EscapeDataString(state)}" : "");

        _logger.LogInformation(
            "OAuth: authorize — code issued; redirect_uri={RedirectUri} state={State} location={Location}",
            redirectUri, state, location);

        return Redirect(location);
    }

    /// <summary>RFC 6749 §4.1.3 / RFC 7636 — Token endpoint. Validates PKCE and issues token.</summary>
    [HttpPost("token")]
    public IActionResult Token([FromForm] TokenRequest request)
    {
        _logger.LogInformation("OAuth: token request; grant_type={GrantType} code={Code}",
            request.GrantType, request.Code ?? "(none)");

        if (request.GrantType == "authorization_code")
        {
            if (string.IsNullOrEmpty(request.Code) || !_codes.TryRemove(request.Code, out var stored))
            {
                _logger.LogWarning("OAuth: token — invalid or unknown code={Code}", request.Code);
                return BadRequest(new { error = "invalid_grant" });
            }

            if (!VerifyPkce(request.CodeVerifier, stored.CodeChallenge))
            {
                _logger.LogWarning("OAuth: token — PKCE verification failed; verifier={Verifier} challenge={Challenge}",
                    request.CodeVerifier, stored.CodeChallenge);
                return BadRequest(new { error = "invalid_grant", error_description = "PKCE verification failed" });
            }

            _logger.LogInformation("OAuth: token — PKCE OK, issuing access token");
        }

        if (!_authOptions.IsEnabled)
        {
            _logger.LogWarning("OAuth: token requested but no API key is configured");
            return BadRequest(new { error = "server_error", error_description = "No API key configured on this server" });
        }

        return Ok(new
        {
            access_token = _authOptions.ApiKey,
            token_type = "Bearer",
            expires_in = 3600
        });
    }

    private static bool VerifyPkce(string? verifier, string challenge)
    {
        if (string.IsNullOrEmpty(verifier)) return false;
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var computed = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return computed == challenge;
    }
}

public sealed class ClientRegistrationRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("redirect_uris")]
    public string[]? RedirectUris { get; set; }
}

public sealed class TokenRequest
{
    [Microsoft.AspNetCore.Mvc.FromForm(Name = "grant_type")]
    public string? GrantType { get; set; }

    [Microsoft.AspNetCore.Mvc.FromForm(Name = "code")]
    public string? Code { get; set; }

    [Microsoft.AspNetCore.Mvc.FromForm(Name = "code_verifier")]
    public string? CodeVerifier { get; set; }

    [Microsoft.AspNetCore.Mvc.FromForm(Name = "redirect_uri")]
    public string? RedirectUri { get; set; }
}
