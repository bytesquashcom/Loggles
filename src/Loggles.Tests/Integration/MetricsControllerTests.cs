using System.Net;
using Xunit;

namespace Loggles.Tests.Integration;

public sealed class MetricsControllerTests
{
    [Fact]
    public async Task PostMetrics_Returns200()
    {
        // Arrange — no API key → auth is disabled, [Authorize] becomes a no-op
        await using var factory = new TestWebAppFactory();
        var client = factory.CreateClient();

        // Act
        var response = await client.PostAsync("/v1/metrics", new StringContent(string.Empty));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
