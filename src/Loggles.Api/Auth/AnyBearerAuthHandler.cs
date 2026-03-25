using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Loggles.Api.Auth;

/// <summary>
/// Local-dev auth handler: authenticates any request that carries a Bearer token.
/// No signature validation — this is intentional for local development only.
/// </summary>
public sealed class AnyBearerAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public AnyBearerAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (authHeader is null || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogWarning("Auth: no Bearer token on {Method} {Path}", Request.Method, Request.Path);
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var tokenPreview = authHeader.Length > 14 ? authHeader[7..14] + "…" : "(empty)";
        Logger.LogInformation("Auth: Bearer token accepted (prefix={TokenPrefix}) on {Method} {Path}",
            tokenPreview, Request.Method, Request.Path);

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "mcp-client")], Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
