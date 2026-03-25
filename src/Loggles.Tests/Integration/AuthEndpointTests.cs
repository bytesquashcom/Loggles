using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Loggles.Tests.Integration;

/// <summary>
/// Verifies API key authentication behaviour:
/// - No key configured → all endpoints accessible without auth
/// - Key configured + correct token → 200/202
/// - Key configured + wrong token → 401
/// - Key configured + no token → 401
/// </summary>
public sealed class AuthEndpointTests
{
    private static readonly object SampleOtlpPayload = new
    {
        resourceLogs = new[]
        {
            new
            {
                resource = new { attributes = new[] { new { key = "service.name", value = new { stringValue = "auth-test" } } } },
                scopeLogs = new[]
                {
                    new
                    {
                        logRecords = new[]
                        {
                            new { timeUnixNano = "1700000000000000000", severityNumber = 9, body = new { stringValue = "auth test event" } }
                        }
                    }
                }
            }
        }
    };

    // ── No API key configured ────────────────────────────────────────────────

    [Fact]
    public async Task NoApiKey_OtlpIngest_WithNoToken_Returns202()
    {
        // Arrange
        await using var factory = new TestWebAppFactory(); // no API key = auth disabled
        var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/v1/logs", SampleOtlpPayload);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task NoApiKey_Search_WithNoToken_Returns200()
    {
        // Arrange
        await using var factory = new TestWebAppFactory(); // no API key = auth disabled
        var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/search", new { from = DateTime.UtcNow.AddMinutes(-1), to = DateTime.UtcNow.AddMinutes(1) });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── API key configured ───────────────────────────────────────────────────

    [Fact]
    public async Task ApiKeySet_OtlpIngest_WithCorrectToken_Returns202()
    {
        // Arrange
        const string key = "test-secret-key";
        await using var factory = new AuthenticatedTestWebAppFactory(key);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {key}");

        // Act
        var response = await client.PostAsJsonAsync("/v1/logs", SampleOtlpPayload);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task ApiKeySet_OtlpIngest_WithWrongToken_Returns401()
    {
        // Arrange
        await using var factory = new AuthenticatedTestWebAppFactory("correct-key");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer wrong-key");

        // Act
        var response = await client.PostAsJsonAsync("/v1/logs", SampleOtlpPayload);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiKeySet_OtlpIngest_WithNoToken_Returns401()
    {
        // Arrange
        await using var factory = new AuthenticatedTestWebAppFactory("some-key");
        var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/v1/logs", SampleOtlpPayload);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiKeySet_Search_WithCorrectToken_Returns200()
    {
        // Arrange
        const string key = "test-secret-key";
        await using var factory = new AuthenticatedTestWebAppFactory(key);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {key}");

        // Act
        var response = await client.PostAsJsonAsync("/search", new { from = DateTime.UtcNow.AddMinutes(-1), to = DateTime.UtcNow.AddMinutes(1) });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ApiKeySet_Search_WithNoToken_Returns401()
    {
        // Arrange
        await using var factory = new AuthenticatedTestWebAppFactory("some-key");
        var client = factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/search", new { from = DateTime.UtcNow.AddMinutes(-1), to = DateTime.UtcNow.AddMinutes(1) });

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── All remaining protected endpoints ────────────────────────────────────

    [Theory]
    [InlineData("GET",  "/logs/999")]
    [InlineData("GET",  "/meta/properties")]
    [InlineData("GET",  "/stats/levels")]
    public async Task ApiKeySet_GetEndpoints_WithNoToken_Returns401(string method, string path)
    {
        // Arrange
        await using var factory = new AuthenticatedTestWebAppFactory("some-key");
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(new HttpMethod(method), path);

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("GET",  "/logs/999")]
    [InlineData("GET",  "/meta/properties")]
    [InlineData("GET",  "/stats/levels")]
    public async Task NoApiKey_GetEndpoints_WithNoToken_ReachEndpoint(string method, string path)
    {
        // Arrange — auth disabled, endpoints should be reachable (2xx or 404, never 401)
        await using var factory = new TestWebAppFactory(); // no API key = auth disabled
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(new HttpMethod(method), path);

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
