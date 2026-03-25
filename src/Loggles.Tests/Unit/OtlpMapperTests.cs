using System.Text.Json.Nodes;
using Loggles.Core.Models;
using Loggles.Core.Services;
using Xunit;

namespace Loggles.Tests.Unit;

public sealed class OtlpMapperTests
{
    private static JsonNode BuildPayload(
        int severityNumber = 9,
        string? body = "hello world",
        string? serviceName = "my-service",
        string? correlationId = null)
    {
        var corrAttr = correlationId != null
            ? $", {{\"key\": \"correlation_id\", \"value\": {{\"stringValue\": \"{correlationId}\"}}}}"
            : "";

        var json = $@"{{
          ""resourceLogs"": [{{
            ""resource"": {{
              ""attributes"": [
                {{""key"": ""service.name"", ""value"": {{""stringValue"": ""{serviceName}""}}}}{corrAttr}
              ]
            }},
            ""scopeLogs"": [{{
              ""logRecords"": [{{
                ""timeUnixNano"": ""1700000000000000000"",
                ""severityNumber"": {severityNumber},
                ""body"": {{""stringValue"": ""{body}""}}
              }}]
            }}]
          }}]
        }}";

        return JsonNode.Parse(json)!;
    }

    [Fact]
    public void Map_ValidPayload_MapsFieldsCorrectly()
    {
        // Arrange
        var payload = BuildPayload(severityNumber: 9, body: "test message", serviceName: "svc-a");

        // Act
        var events = OtlpMapper.Map(payload);

        // Assert
        Assert.Single(events);
        var evt = events[0];
        Assert.Equal("test message", evt.Message);
        Assert.Equal(LogLevel.Information, evt.Level);
        Assert.Equal("svc-a", evt.Source);
    }

    [Fact]
    public void Map_MissingSeverity_DefaultsToInformation()
    {
        // Arrange
        var payload = BuildPayload(severityNumber: 0);

        // Act
        var events = OtlpMapper.Map(payload);

        // Assert
        Assert.Equal(LogLevel.Information, events[0].Level);
    }

    [Fact]
    public void Map_MissingBody_SetsEmptyMessage()
    {
        // Arrange
        var payload = JsonNode.Parse("""
        {
          "resourceLogs": [{
            "resource": {"attributes": []},
            "scopeLogs": [{
              "logRecords": [{"timeUnixNano": "1700000000000000000", "severityNumber": 9}]
            }]
          }]
        }
        """)!;

        // Act
        var events = OtlpMapper.Map(payload);

        // Assert
        Assert.Single(events);
        Assert.Equal(string.Empty, events[0].Message);
    }

    [Fact]
    public void Map_WithCorrelationId_MapsCorrelationId()
    {
        // Arrange
        var payload = BuildPayload(correlationId: "abc-123");

        // Act
        var events = OtlpMapper.Map(payload);

        // Assert
        Assert.Equal("abc-123", events[0].CorrelationId);
    }

    [Theory]
    [InlineData(1, LogLevel.Trace)]
    [InlineData(5, LogLevel.Debug)]
    [InlineData(9, LogLevel.Information)]
    [InlineData(13, LogLevel.Warning)]
    [InlineData(17, LogLevel.Error)]
    [InlineData(21, LogLevel.Critical)]
    public void Map_SeverityNumber_MapsToCorrectLevel(int severityNumber, LogLevel expected)
    {
        // Arrange
        var payload = BuildPayload(severityNumber: severityNumber);

        // Act
        var events = OtlpMapper.Map(payload);

        // Assert
        Assert.Equal(expected, events[0].Level);
    }

    [Fact]
    public void Map_EmptyResourceLogs_ReturnsEmpty()
    {
        // Arrange
        var payload = JsonNode.Parse("""{"resourceLogs": []}""")!;

        // Act
        var events = OtlpMapper.Map(payload);

        // Assert
        Assert.Empty(events);
    }

    [Fact]
    public void Map_OriginalFormatAttribute_MappedToMessageTemplate()
    {
        // Arrange
        var payload = JsonNode.Parse("""
        {
          "resourceLogs": [{
            "resource": {"attributes": []},
            "scopeLogs": [{
              "logRecords": [{
                "timeUnixNano": "1700000000000000000",
                "severityNumber": 9,
                "body": {"stringValue": "Hello World"},
                "attributes": [{"key": "{OriginalFormat}", "value": {"stringValue": "Hello {Name}"}}]
              }]
            }]
          }]
        }
        """)!;

        // Act
        var events = OtlpMapper.Map(payload);

        // Assert
        Assert.Equal("Hello {Name}", events[0].MessageTemplate);
    }

    [Fact]
    public void Map_OriginalFormatAttribute_ExcludedFromPropertiesJson()
    {
        // Arrange
        var payload = JsonNode.Parse("""
        {
          "resourceLogs": [{
            "resource": {"attributes": []},
            "scopeLogs": [{
              "logRecords": [{
                "timeUnixNano": "1700000000000000000",
                "severityNumber": 9,
                "body": {"stringValue": "Hello World"},
                "attributes": [
                  {"key": "{OriginalFormat}", "value": {"stringValue": "Hello {Name}"}},
                  {"key": "Name", "value": {"stringValue": "World"}}
                ]
              }]
            }]
          }]
        }
        """)!;

        // Act
        var events = OtlpMapper.Map(payload);

        // Assert
        Assert.DoesNotContain("{OriginalFormat}", events[0].PropertiesJson ?? "");
        Assert.Contains("Name", events[0].PropertiesJson!);
    }

    private static JsonNode BuildPayloadWithResourceAttr(string key, string value) =>
        JsonNode.Parse(
            $@"{{""resourceLogs"":[{{""resource"":{{""attributes"":[{{""key"":""{key}"",""value"":{{""stringValue"":""{value}""}}}},{{""key"":""custom.field"",""value"":{{""stringValue"":""kept""}}}}]}},""scopeLogs"":[{{""logRecords"":[{{""timeUnixNano"":""1700000000000000000"",""severityNumber"":9,""body"":{{""stringValue"":""msg""}}}}]}}]}}]}}")!;

    [Theory]
    [InlineData("telemetry.sdk.name")]
    [InlineData("telemetry.sdk.language")]
    [InlineData("telemetry.sdk.version")]
    [InlineData("service.instance.id")]
    public void Map_StaticOtelSdkAttributes_ExcludedFromPropertiesJson(string key)
    {
        // Arrange
        var payload = BuildPayloadWithResourceAttr(key, "noise");

        // Act
        var events = OtlpMapper.Map(payload);

        // Assert
        Assert.DoesNotContain(key, events[0].PropertiesJson ?? "");
        Assert.Contains("custom.field", events[0].PropertiesJson!);
    }

    [Fact]
    public void Map_ParentIdAllZeros_ExcludedFromPropertiesJson()
    {
        // Arrange
        var payload = JsonNode.Parse("""
        {
          "resourceLogs": [{
            "resource": {"attributes": []},
            "scopeLogs": [{
              "logRecords": [{
                "timeUnixNano": "1700000000000000000",
                "severityNumber": 9,
                "body": {"stringValue": "msg"},
                "attributes": [{"key": "ParentId", "value": {"stringValue": "0000000000000000"}}]
              }]
            }]
          }]
        }
        """)!;

        // Act
        var events = OtlpMapper.Map(payload);

        // Assert
        Assert.True(events[0].PropertiesJson is null || !events[0].PropertiesJson.Contains("ParentId"));
    }

    [Fact]
    public void Map_ParentIdWithRealValue_RetainedInPropertiesJson()
    {
        // Arrange
        var payload = JsonNode.Parse("""
        {
          "resourceLogs": [{
            "resource": {"attributes": []},
            "scopeLogs": [{
              "logRecords": [{
                "timeUnixNano": "1700000000000000000",
                "severityNumber": 9,
                "body": {"stringValue": "msg"},
                "attributes": [{"key": "ParentId", "value": {"stringValue": "abc123def456789a"}}]
              }]
            }]
          }]
        }
        """)!;

        // Act
        var events = OtlpMapper.Map(payload);

        // Assert
        Assert.Contains("ParentId", events[0].PropertiesJson!);
        Assert.Contains("abc123def456789a", events[0].PropertiesJson!);
    }

    [Fact]
    public void Map_AdditionalAttributes_StoredInPropertiesJson()
    {
        // Arrange
        var payload = JsonNode.Parse("""
        {
          "resourceLogs": [{
            "resource": {"attributes": [{"key": "region", "value": {"stringValue": "eu-west-1"}}]},
            "scopeLogs": [{
              "logRecords": [{
                "timeUnixNano": "1700000000000000000",
                "severityNumber": 9,
                "body": {"stringValue": "msg"}
              }]
            }]
          }]
        }
        """)!;

        // Act
        var events = OtlpMapper.Map(payload);

        // Assert
        Assert.NotNull(events[0].PropertiesJson);
        Assert.Contains("region", events[0].PropertiesJson!);
        Assert.Contains("eu-west-1", events[0].PropertiesJson!);
    }
}
