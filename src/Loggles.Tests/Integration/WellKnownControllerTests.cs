using System.Net;
using System.Text.Json;
using Xunit;

namespace Loggles.Tests.Integration;

/// <summary>
/// Verifies RFC 8414 discovery endpoint behaviour under different configurations:
/// - OAuthEnabled=true (default)  → endpoints present and return expected fields
/// - OAuthEnabled=false           → endpoints absent (404)
/// </summary>
public sealed class WellKnownControllerTests
{
    // ── OAuthEnabled = true (default) ────────────────────────────────────────

    [Fact]
    public async Task OAuthEnabled_AuthServerMetadata_Returns200()
    {
        // Arrange
        await using var factory = new OAuthEnabledTestWebAppFactory();
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/.well-known/oauth-authorization-server");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OAuthEnabled_AuthServerMetadata_ContainsRequiredFields()
    {
        // Arrange
        await using var factory = new OAuthEnabledTestWebAppFactory();
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/.well-known/oauth-authorization-server");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        // Assert — RFC 8414 required fields
        Assert.True(root.TryGetProperty("issuer", out _), "missing: issuer");
        Assert.True(root.TryGetProperty("authorization_endpoint", out _), "missing: authorization_endpoint");
        Assert.True(root.TryGetProperty("token_endpoint", out _), "missing: token_endpoint");
        Assert.True(root.TryGetProperty("code_challenge_methods_supported", out var pkce), "missing: code_challenge_methods_supported");
        Assert.Contains("S256", pkce.EnumerateArray().Select(e => e.GetString()));
        Assert.True(root.TryGetProperty("registration_endpoint", out _), "missing: registration_endpoint");
    }

    [Fact]
    public async Task OAuthEnabled_AuthServerMetadata_EndpointsPointToSameHost()
    {
        // Arrange
        await using var factory = new OAuthEnabledTestWebAppFactory();
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/.well-known/oauth-authorization-server");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        var issuer = root.GetProperty("issuer").GetString()!;
        var tokenEndpoint = root.GetProperty("token_endpoint").GetString()!;
        var authEndpoint = root.GetProperty("authorization_endpoint").GetString()!;
        var registrationEndpoint = root.GetProperty("registration_endpoint").GetString()!;

        // Assert — all URLs share the same base
        Assert.StartsWith(issuer, tokenEndpoint);
        Assert.StartsWith(issuer, authEndpoint);
        Assert.StartsWith(issuer, registrationEndpoint);
    }

    [Fact]
    public async Task OAuthEnabled_ProtectedResourceMetadata_Returns200()
    {
        // Arrange
        await using var factory = new OAuthEnabledTestWebAppFactory();
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/.well-known/oauth-protected-resource");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OAuthEnabled_ProtectedResourceMetadata_ContainsRequiredFields()
    {
        // Arrange
        await using var factory = new OAuthEnabledTestWebAppFactory();
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/.well-known/oauth-protected-resource");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        // Assert
        Assert.True(root.TryGetProperty("resource", out _), "missing: resource");
        Assert.True(root.TryGetProperty("authorization_servers", out var servers), "missing: authorization_servers");
        Assert.True(servers.GetArrayLength() > 0, "authorization_servers must not be empty");
    }

    // ── OAuthEnabled = false ─────────────────────────────────────────────────

    [Fact]
    public async Task OAuthDisabled_AuthServerMetadata_Returns404()
    {
        // Arrange
        await using var factory = new OAuthDisabledTestWebAppFactory();
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/.well-known/oauth-authorization-server");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OAuthDisabled_ProtectedResourceMetadata_Returns404()
    {
        // Arrange
        await using var factory = new OAuthDisabledTestWebAppFactory();
        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/.well-known/oauth-protected-resource");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
