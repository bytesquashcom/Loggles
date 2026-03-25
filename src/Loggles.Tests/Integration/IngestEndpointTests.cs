using System.Net;
using System.Net.Http.Json;
using Loggles.Core.DTOs;
using Loggles.Core.Interfaces;
using Loggles.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Loggles.Tests.Integration;

public sealed class IngestEndpointTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly ILogStore _store;

    public IngestEndpointTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
        _store = factory.Services.GetRequiredService<ILogStore>();
    }

    [Fact]
    public async Task PostOtlpIngest_ValidPayload_Returns202()
    {
        // Arrange
        var otlpPayload = new
        {
            resourceLogs = new[]
            {
                new
                {
                    resource = new
                    {
                        attributes = new[] { new { key = "service.name", value = new { stringValue = "otlp-svc" } } }
                    },
                    scopeLogs = new[]
                    {
                        new
                        {
                            logRecords = new[]
                            {
                                new
                                {
                                    timeUnixNano = "1700000000000000000",
                                    severityNumber = 9,
                                    body = new { stringValue = "otlp test message" }
                                }
                            }
                        }
                    }
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/v1/logs", otlpPayload);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task DirectWrite_EventsStoredAndQueryableViaStore()
    {
        // Arrange
        var ts = DateTime.UtcNow;
        await _store.WriteAsync([new LogEvent
        {
            Timestamp = ts,
            Level = LogLevel.Warning,
            Source = "direct-write",
            Message = "direct write test"
        }]);

        // Act
        var result = await _store.SearchAsync(new SearchQuery
        {
            From = ts.AddSeconds(-1),
            To = ts.AddSeconds(1),
            Source = "direct-write"
        });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("direct write test", result.Items[0].Message);
    }
}
