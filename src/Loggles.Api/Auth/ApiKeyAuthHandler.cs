using System.Security.Claims;
using System.Text.Encodings.Web;
using Loggles.Api.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Loggles.Api.Auth;

/// <summary>
/// Bearer token handler that validates the token against the configured API key.
/// Rejects requests with a missing or incorrect token with 401.
/// </summary>
public sealed class ApiKeyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly AuthOptions _authOptions;

    public ApiKeyAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<AuthOptions> authOptions)
        : base(options, logger, encoder)
    {
        _authOptions = authOptions.Value;
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var resourceMetadata = $"{Request.Scheme}://{Request.Host}/.well-known/oauth-protected-resource";
        Response.StatusCode = 401;
        Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{resourceMetadata}\"";
        return Task.CompletedTask;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // When auth is disabled, skip validation entirely — no warnings, no 401s.
        if (!_authOptions.IsEnabled)
            return Task.FromResult(AuthenticateResult.NoResult());

        // Accept token from Authorization header or ?access_token= query param (for EventSource clients).
        string? token = null;
        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (authHeader is not null && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = authHeader["Bearer ".Length..].Trim();
        }
        else if (Request.Query.TryGetValue("access_token", out var qt) && !string.IsNullOrEmpty(qt))
        {
            token = qt!;
        }

        if (token is null)
        {
            Logger.LogWarning("Auth: missing Bearer token on {Method} {Path}", Request.Method, Request.Path);
            return Task.FromResult(AuthenticateResult.Fail("Missing Bearer token"));
        }
        if (!string.Equals(token, _authOptions.ApiKey, StringComparison.Ordinal))
        {
            Logger.LogWarning("Auth: invalid API key on {Method} {Path}", Request.Method, Request.Path);
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "api-client")], Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
