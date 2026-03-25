using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Loggles.Tests.Integration;

/// <summary>
/// Tests for the OAuth 2.0 + PKCE flow endpoints:
///   POST /oauth/register  — dynamic client registration (RFC 7591)
///   GET  /oauth/authorize — authorization endpoint (RFC 6749 §4.1.2)
///   POST /oauth/token     — token endpoint with PKCE (RFC 7636)
/// </summary>
public sealed class OAuthFlowTests
{
    // ── /oauth/register ──────────────────────────────────────────────────────

    [Fact]
    public async Task Register_Returns201WithClientId()
    {
        // Arrange
        await using var factory = new OAuthEnabledTestWebAppFactory();
        var client = factory.CreateClient();

        var body = new { redirect_uris = new[] { "http://localhost:9999/callback" } };

        // Act
        var response = await client.PostAsJsonAsync("/oauth/register", body);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(json.TryGetProperty("client_id", out var clientId));
        Assert.False(string.IsNullOrEmpty(clientId.GetString()));
    }

    [Fact]
    public async Task Register_ReturnsGrantTypeAuthorizationCode()
    {
        // Arrange
        await using var factory = new OAuthEnabledTestWebAppFactory();
        var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/oauth/register",
            new { redirect_uris = new[] { "http://localhost/cb" } });
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        // Assert
        var grantTypes = json.GetProperty("grant_types").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("authorization_code", grantTypes);
    }

    [Fact]
    public async Task OAuthDisabled_Register_Returns404()
    {
        // Arrange
        await using var factory = new OAuthDisabledTestWebAppFactory();
        var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/oauth/register",
            new { redirect_uris = new[] { "http://localhost/cb" } });

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── /oauth/authorize ─────────────────────────────────────────────────────

    [Fact]
    public async Task Authorize_RedirectsWithCode()
    {
        // Arrange
        await using var factory = new OAuthEnabledTestWebAppFactory();
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var (_, challenge) = GeneratePkce();

        // Act
        var response = await client.GetAsync(
            $"/oauth/authorize?redirect_uri={Uri.EscapeDataString("http://localhost/cb")}" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}" +
            $"&code_challenge_method=S256" +
            $"&state=test-state");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.Contains("code=", location);
    }

    [Fact]
    public async Task Authorize_PreservesStateInRedirect()
    {
        // Arrange
        await using var factory = new OAuthEnabledTestWebAppFactory();
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var (_, challenge) = GeneratePkce();
        const string state = "my-unique-state-value";

        // Act
        var response = await client.GetAsync(
            $"/oauth/authorize?redirect_uri={Uri.EscapeDataString("http://localhost/cb")}" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}" +
            $"&state={Uri.EscapeDataString(state)}");

        // Assert
        var location = response.Headers.Location!.ToString();
        Assert.Contains($"state={state}", location);
    }

    // ── /oauth/token ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Token_WithValidPkce_ReturnsConfiguredApiKey()
    {
        // Arrange
        const string apiKey = "my-configured-api-key";
        await using var factory = new AuthenticatedTestWebAppFactory(apiKey);
        var noRedirectClient = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var tokenClient = factory.CreateClient();

        var (verifier, challenge) = GeneratePkce();

        // Step 1: authorize → get code
        var authResponse = await noRedirectClient.GetAsync(
            $"/oauth/authorize?redirect_uri={Uri.EscapeDataString("http://localhost/cb")}" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}" +
            $"&state=s");
        var location = authResponse.Headers.Location!.ToString();
        var code = ParseQueryParam(location, "code");

        // Step 2: exchange code for token
        var tokenResponse = await tokenClient.PostAsync("/oauth/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"]    = "authorization_code",
                ["code"]          = code,
                ["code_verifier"] = verifier,
                ["redirect_uri"]  = "http://localhost/cb"
            }));

        // Assert
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var json = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(apiKey, json.GetProperty("access_token").GetString());
        Assert.Equal("Bearer", json.GetProperty("token_type").GetString());
    }

    [Fact]
    public async Task Token_WithInvalidCode_ReturnsInvalidGrant()
    {
        // Arrange
        await using var factory = new AuthenticatedTestWebAppFactory("some-key");
        var client = factory.CreateClient();
        var (verifier, _) = GeneratePkce();

        // Act
        var response = await client.PostAsync("/oauth/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"]    = "authorization_code",
                ["code"]          = "this-code-does-not-exist",
                ["code_verifier"] = verifier
            }));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("invalid_grant", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Token_WithWrongVerifier_ReturnsInvalidGrant()
    {
        // Arrange
        await using var factory = new AuthenticatedTestWebAppFactory("some-key");
        var noRedirectClient = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var tokenClient = factory.CreateClient();

        var (_, challenge) = GeneratePkce();

        // Get a valid code
        var authResponse = await noRedirectClient.GetAsync(
            $"/oauth/authorize?redirect_uri={Uri.EscapeDataString("http://localhost/cb")}" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}");
        var code = ParseQueryParam(authResponse.Headers.Location!.ToString(), "code");

        // Act — send a different verifier
        var response = await tokenClient.PostAsync("/oauth/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"]    = "authorization_code",
                ["code"]          = code,
                ["code_verifier"] = "wrong-verifier-that-does-not-match"
            }));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("invalid_grant", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Token_CodeCanOnlyBeUsedOnce()
    {
        // Arrange
        const string apiKey = "once-key";
        await using var factory = new AuthenticatedTestWebAppFactory(apiKey);
        var noRedirectClient = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var tokenClient = factory.CreateClient();

        var (verifier, challenge) = GeneratePkce();
        var authResponse = await noRedirectClient.GetAsync(
            $"/oauth/authorize?redirect_uri={Uri.EscapeDataString("http://localhost/cb")}" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}");
        var code = ParseQueryParam(authResponse.Headers.Location!.ToString(), "code");

        var formContent = () => new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"]    = "authorization_code",
            ["code"]          = code,
            ["code_verifier"] = verifier
        });

        // Act
        var first  = await tokenClient.PostAsync("/oauth/token", formContent());
        var second = await tokenClient.PostAsync("/oauth/token", formContent());

        // Assert
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Token_WithNoApiKeyConfigured_ReturnsServerError()
    {
        // Arrange — no API key set, but we go through the authorize flow
        await using var factory = new OAuthEnabledTestWebAppFactory();
        var noRedirectClient = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var tokenClient = factory.CreateClient();

        var (verifier, challenge) = GeneratePkce();
        var authResponse = await noRedirectClient.GetAsync(
            $"/oauth/authorize?redirect_uri={Uri.EscapeDataString("http://localhost/cb")}" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}");
        var code = ParseQueryParam(authResponse.Headers.Location!.ToString(), "code");

        // Act
        var response = await tokenClient.PostAsync("/oauth/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"]    = "authorization_code",
                ["code"]          = code,
                ["code_verifier"] = verifier
            }));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("server_error", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task OAuthDisabled_Token_Returns404()
    {
        // Arrange
        await using var factory = new OAuthDisabledTestWebAppFactory();
        var client = factory.CreateClient();

        // Act
        var response = await client.PostAsync("/oauth/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"]       = "irrelevant"
            }));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── End-to-end: token used on protected endpoint ─────────────────────────

    [Fact]
    public async Task Token_WhenUsedOnProtectedEndpoint_Returns200()
    {
        // Arrange
        const string apiKey = "e2e-test-api-key";
        await using var factory = new AuthenticatedTestWebAppFactory(apiKey);
        var noRedirectClient = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var apiClient = factory.CreateClient();

        var (verifier, challenge) = GeneratePkce();

        // Step 1: authorize → get code
        var authResponse = await noRedirectClient.GetAsync(
            $"/oauth/authorize?redirect_uri={Uri.EscapeDataString("http://localhost/cb")}" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}" +
            $"&state=e2e");
        var code = ParseQueryParam(authResponse.Headers.Location!.ToString(), "code");

        // Step 2: exchange code for token
        var tokenResponse = await apiClient.PostAsync("/oauth/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"]    = "authorization_code",
                ["code"]          = code,
                ["code_verifier"] = verifier,
                ["redirect_uri"]  = "http://localhost/cb"
            }));
        var json = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync()).RootElement;
        var accessToken = json.GetProperty("access_token").GetString();

        // Step 3: call protected endpoint with token
        var request = new HttpRequestMessage(HttpMethod.Post, "/search");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = System.Net.Http.Json.JsonContent.Create(new
        {
            from = DateTime.UtcNow.AddMinutes(-1),
            to   = DateTime.UtcNow.AddMinutes(1)
        });

        // Act
        var searchResponse = await apiClient.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
    }

    [Fact]
    public async Task Token_WhenUsedOnProtectedEndpoint_WithWrongToken_Returns401()
    {
        // Arrange
        const string apiKey = "e2e-test-api-key-2";
        await using var factory = new AuthenticatedTestWebAppFactory(apiKey);
        var apiClient = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/search");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "garbage-token");
        request.Content = System.Net.Http.Json.JsonContent.Create(new
        {
            from = DateTime.UtcNow.AddMinutes(-1),
            to   = DateTime.UtcNow.AddMinutes(1)
        });

        // Act
        var response = await apiClient.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Generates a PKCE (verifier, S256 challenge) pair.</summary>
    private static (string Verifier, string Challenge) GeneratePkce()
    {
        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (verifier, challenge);
    }

    private static string ParseQueryParam(string url, string key)
    {
        var query = new Uri(url, UriKind.RelativeOrAbsolute).IsAbsoluteUri
            ? new Uri(url).Query
            : url[(url.IndexOf('?'))..];
        return System.Web.HttpUtility.ParseQueryString(query)[key]!;
    }
}
