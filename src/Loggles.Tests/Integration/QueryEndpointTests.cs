using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Loggles.Core.DTOs;
using Loggles.Core.Interfaces;
using Loggles.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Loggles.Tests.Integration;

public sealed class QueryEndpointTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly ILogStore _store;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public QueryEndpointTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
        _store = factory.Services.GetRequiredService<ILogStore>();
    }

    private async Task<long> SeedEventAsync(string message = "seeded", LogLevel level = LogLevel.Information)
    {
        var ts = DateTime.UtcNow;
        await _store.WriteAsync([new LogEvent
        {
            Timestamp = ts,
            Level = level,
            Source = "query-test-svc",
            Message = message
        }]);
        var result = await _store.SearchAsync(new SearchQuery
        {
            From = ts.AddSeconds(-1),
            To = ts.AddSeconds(1),
            Text = message
        });
        return result.Items[0].Id;
    }

    [Fact]
    public async Task PostSearch_ReturnsMatchingEvents()
    {
        // Arrange
        await SeedEventAsync("queryable event");

        var query = new
        {
            from = DateTime.UtcNow.AddMinutes(-1).ToString("o"),
            to = DateTime.UtcNow.AddMinutes(1).ToString("o"),
            text = "queryable event"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/search", query);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("queryable event", content);
    }

    [Fact]
    public async Task PostSearch_WithPagination_ReturnsNextPageToken()
    {
        // Arrange — seed 5 events with unique prefix to isolate this test
        var prefix = Guid.NewGuid().ToString("N")[..8];
        for (int i = 0; i < 5; i++)
            await SeedEventAsync($"{prefix}-event-{i}");

        var query = new
        {
            from = DateTime.UtcNow.AddMinutes(-1).ToString("o"),
            to = DateTime.UtcNow.AddMinutes(1).ToString("o"),
            text = prefix,
            pageSize = 3
        };

        // Act
        var response = await _client.PostAsJsonAsync("/search", query);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, body.RootElement.GetProperty("items").GetArrayLength());
        Assert.True(body.RootElement.TryGetProperty("nextPageToken", out var tok) && tok.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task GetLogById_ExistingId_ReturnsEvent()
    {
        // Arrange
        var id = await SeedEventAsync("get by id test");

        // Act
        var response = await _client.GetAsync($"/logs/{id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("get by id test", content);
    }

    [Fact]
    public async Task GetLogById_NonExistentId_Returns404()
    {
        // Act
        var response = await _client.GetAsync("/logs/999999999");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetStatsLevels_ReturnsCounts()
    {
        // Arrange
        await SeedEventAsync("info event", LogLevel.Information);
        await SeedEventAsync("error event", LogLevel.Error);

        // Act
        var from = DateTime.UtcNow.AddMinutes(-1).ToString("o");
        var to = DateTime.UtcNow.AddMinutes(1).ToString("o");
        var response = await _client.GetAsync($"/stats/levels?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.ValueKind == JsonValueKind.Object);
    }
}
