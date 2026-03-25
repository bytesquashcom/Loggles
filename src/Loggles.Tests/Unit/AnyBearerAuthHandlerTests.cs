using System.Security.Claims;
using System.Text.Encodings.Web;
using Loggles.Api.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Loggles.Tests.Unit;

public sealed class AnyBearerAuthHandlerTests
{
    private static async Task<AuthenticateResult> AuthenticateAsync(string? authHeader)
    {
        var scheme = new AuthenticationScheme("Bearer", null, typeof(AnyBearerAuthHandler));
        var monitor = new TestOptionsMonitor(new AuthenticationSchemeOptions());

        var handler = new AnyBearerAuthHandler(monitor, NullLoggerFactory.Instance, UrlEncoder.Default);

        var context = new DefaultHttpContext();
        if (authHeader is not null)
            context.Request.Headers.Authorization = authHeader;

        await handler.InitializeAsync(scheme, context);
        return await handler.AuthenticateAsync();
    }

    [Fact]
    public async Task ValidBearerToken_ReturnsSuccess()
    {
        var result = await AuthenticateAsync("Bearer any-token-value");
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task MissingAuthorizationHeader_ReturnsNoResult()
    {
        var result = await AuthenticateAsync(null);
        Assert.False(result.Succeeded);
        Assert.True(result.None);
    }

    [Fact]
    public async Task NonBearerScheme_ReturnsNoResult()
    {
        var result = await AuthenticateAsync("Basic abc");
        Assert.False(result.Succeeded);
        Assert.True(result.None);
    }

    [Fact]
    public async Task ClaimsPrincipal_HasMcpClientName()
    {
        var result = await AuthenticateAsync("Bearer some-token");
        Assert.True(result.Succeeded);
        var name = result.Principal!.FindFirst(ClaimTypes.Name)?.Value;
        Assert.Equal("mcp-client", name);
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public TestOptionsMonitor(AuthenticationSchemeOptions value) => CurrentValue = value;
        public AuthenticationSchemeOptions CurrentValue { get; }
        public AuthenticationSchemeOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }
}
