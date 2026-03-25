using System.Text.Encodings.Web;
using Loggles.Api.Auth;
using Loggles.Api.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Loggles.Tests.Unit;

public sealed class ApiKeyAuthHandlerTests
{
    private static async Task<AuthenticateResult> AuthenticateAsync(string? configuredKey, string? authHeader)
    {
        var authOptions = new AuthOptions { ApiKey = configuredKey };

        var scheme = new AuthenticationScheme("Bearer", null, typeof(ApiKeyAuthHandler));
        var schemeOptions = Options.Create(new AuthenticationSchemeOptions());
        var monitor = new TestOptionsMonitor(schemeOptions.Value);

        var handler = new ApiKeyAuthHandler(
            monitor,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            Options.Create(authOptions));

        var context = new DefaultHttpContext();
        if (authHeader is not null)
            context.Request.Headers.Authorization = authHeader;

        await handler.InitializeAsync(scheme, context);
        return await handler.AuthenticateAsync();
    }

    [Fact]
    public async Task CorrectKey_ReturnsSuccess()
    {
        var result = await AuthenticateAsync("my-secret", "Bearer my-secret");
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task WrongKey_ReturnsFail()
    {
        var result = await AuthenticateAsync("my-secret", "Bearer wrong-key");
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public async Task MissingHeader_ReturnsFail()
    {
        var result = await AuthenticateAsync("my-secret", null);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public async Task NonBearerHeader_ReturnsFail()
    {
        var result = await AuthenticateAsync("my-secret", "Basic my-secret");
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task KeyIsCaseSensitive()
    {
        var result = await AuthenticateAsync("MySecret", "Bearer mysecret");
        Assert.False(result.Succeeded);
    }

    // Helper: IOptionsMonitor<T> stub
    private sealed class TestOptionsMonitor : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public TestOptionsMonitor(AuthenticationSchemeOptions value) => CurrentValue = value;
        public AuthenticationSchemeOptions CurrentValue { get; }
        public AuthenticationSchemeOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }
}
