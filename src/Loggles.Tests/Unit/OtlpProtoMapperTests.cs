using Loggles.Core.Models;
using Loggles.Core.Protos.Collector.Logs.V1;
using Loggles.Core.Protos.Common.V1;
using Loggles.Core.Protos.Logs.V1;
using Loggles.Core.Protos.Resource.V1;
using Loggles.Core.Services;
using Xunit;

namespace Loggles.Tests.Unit;

public sealed class OtlpProtoMapperTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static KeyValue StrAttr(string key, string value) => new()
    {
        Key = key,
        Value = new AnyValue { StringValue = value }
    };

    private static ExportLogsServiceRequest BuildRequest(
        ulong timeUnixNano = 1_700_000_000_000_000_000UL,
        ulong observedTimeUnixNano = 0,
        int severityNumber = 9,
        string body = "test message",
        string? serviceName = "my-service",
        string? scopeName = null,
        IEnumerable<KeyValue>? resourceAttrs = null,
        IEnumerable<KeyValue>? scopeAttrs = null,
        IEnumerable<KeyValue>? logAttrs = null)
    {
        var resource = new Resource();
        if (serviceName is not null)
            resource.Attributes.Add(StrAttr("service.name", serviceName));
        if (resourceAttrs is not null)
            resource.Attributes.AddRange(resourceAttrs);

        var logRecord = new LogRecord
        {
            TimeUnixNano = timeUnixNano,
            ObservedTimeUnixNano = observedTimeUnixNano,
            SeverityNumber = (SeverityNumber)severityNumber,
            Body = new AnyValue { StringValue = body }
        };
        if (logAttrs is not null)
            logRecord.Attributes.AddRange(logAttrs);

        var scope = new InstrumentationScope { Name = scopeName ?? string.Empty };
        if (scopeAttrs is not null)
            scope.Attributes.AddRange(scopeAttrs);

        var scopeLogs = new ScopeLogs { Scope = scope };
        scopeLogs.LogRecords.Add(logRecord);

        var resourceLogs = new ResourceLogs { Resource = resource };
        resourceLogs.ScopeLogs.Add(scopeLogs);

        var request = new ExportLogsServiceRequest();
        request.ResourceLogs.Add(resourceLogs);
        return request;
    }

    // ── Basic field mapping ──────────────────────────────────────────────────

    [Fact]
    public void Map_ValidRequest_MapsMessageAndSource()
    {
        // Arrange
        var request = BuildRequest(body: "hello world", serviceName: "svc-a");

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.Single(events);
        Assert.Equal("hello world", events[0].Message);
        Assert.Equal("svc-a", events[0].Source);
    }

    [Fact]
    public void Map_EmptyResourceLogs_ReturnsEmpty()
    {
        // Act
        var events = OtlpProtoMapper.Map(new ExportLogsServiceRequest());

        // Assert
        Assert.Empty(events);
    }

    [Fact]
    public void Map_NullResource_UsesUnknownSource()
    {
        // Arrange
        var logRecord = new LogRecord
        {
            TimeUnixNano = 1_700_000_000_000_000_000UL,
            SeverityNumber = SeverityNumber.Info,
            Body = new AnyValue { StringValue = "msg" }
        };
        var scopeLogs = new ScopeLogs();
        scopeLogs.LogRecords.Add(logRecord);
        var resourceLogs = new ResourceLogs(); // Resource is null
        resourceLogs.ScopeLogs.Add(scopeLogs);
        var request = new ExportLogsServiceRequest();
        request.ResourceLogs.Add(resourceLogs);

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.Single(events);
        Assert.Equal("unknown", events[0].Source);
    }

    [Fact]
    public void Map_MissingServiceName_FallsBackToUnknown()
    {
        // Arrange
        var request = BuildRequest(serviceName: null);

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.Equal("unknown", events[0].Source);
    }

    // ── Timestamp ────────────────────────────────────────────────────────────

    [Fact]
    public void Map_TimeUnixNano_ParsedCorrectly()
    {
        // Arrange — 1700000000 seconds since epoch = 2023-11-14T22:13:20Z
        var request = BuildRequest(timeUnixNano: 1_700_000_000_000_000_000UL);

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.Equal(new DateTime(2023, 11, 14, 22, 13, 20, DateTimeKind.Utc), events[0].Timestamp);
    }

    [Fact]
    public void Map_TimeUnixNanoZero_FallsBackToObservedTime()
    {
        // Arrange
        var request = BuildRequest(
            timeUnixNano: 0,
            observedTimeUnixNano: 1_700_000_000_000_000_000UL);

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.Equal(new DateTime(2023, 11, 14, 22, 13, 20, DateTimeKind.Utc), events[0].Timestamp);
    }

    [Fact]
    public void Map_BothTimestampsZero_UsesCurrentTime()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);
        var request = BuildRequest(timeUnixNano: 0, observedTimeUnixNano: 0);

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.True(events[0].Timestamp >= before);
        Assert.True(events[0].Timestamp <= DateTime.UtcNow.AddSeconds(1));
    }

    // ── Severity → LogLevel ──────────────────────────────────────────────────

    [Theory]
    [InlineData(1,  LogLevel.Trace)]
    [InlineData(4,  LogLevel.Trace)]
    [InlineData(5,  LogLevel.Debug)]
    [InlineData(8,  LogLevel.Debug)]
    [InlineData(9,  LogLevel.Information)]
    [InlineData(12, LogLevel.Information)]
    [InlineData(13, LogLevel.Warning)]
    [InlineData(16, LogLevel.Warning)]
    [InlineData(17, LogLevel.Error)]
    [InlineData(20, LogLevel.Error)]
    [InlineData(21, LogLevel.Critical)]
    [InlineData(24, LogLevel.Critical)]
    [InlineData(0,  LogLevel.Information)] // unspecified → default
    public void Map_SeverityNumber_MapsToCorrectLevel(int severityNumber, LogLevel expected)
    {
        // Arrange
        var request = BuildRequest(severityNumber: severityNumber);

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.Equal(expected, events[0].Level);
    }

    // ── Logger name ──────────────────────────────────────────────────────────

    [Fact]
    public void Map_ScopeName_MappedToLoggerName()
    {
        // Arrange
        var request = BuildRequest(scopeName: "MyApp.Services.OrderService");

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.Equal("MyApp.Services.OrderService", events[0].LoggerName);
    }

    [Fact]
    public void Map_EmptyScopeName_LoggerNameIsNull()
    {
        // Arrange
        var request = BuildRequest(scopeName: "");

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.Null(events[0].LoggerName);
    }

    // ── Well-known attributes ────────────────────────────────────────────────

    [Fact]
    public void Map_CorrelationIdAttribute_MappedToCorrelationId()
    {
        // Arrange
        var request = BuildRequest(logAttrs: [StrAttr("correlation_id", "trace-abc")]);

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.Equal("trace-abc", events[0].CorrelationId);
    }

    [Fact]
    public void Map_ExceptionStacktrace_MappedToException()
    {
        // Arrange
        var request = BuildRequest(logAttrs: [StrAttr("exception.stacktrace", "System.Exception: boom")]);

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.Equal("System.Exception: boom", events[0].Exception);
    }

    [Fact]
    public void Map_ExceptionMessage_UsedWhenStacktraceAbsent()
    {
        // Arrange
        var request = BuildRequest(logAttrs: [StrAttr("exception.message", "boom")]);

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.Equal("boom", events[0].Exception);
    }

    [Fact]
    public void Map_ExceptionStacktraceTakesPrecedenceOverMessage()
    {
        // Arrange
        var request = BuildRequest(logAttrs:
        [
            StrAttr("exception.stacktrace", "full stack"),
            StrAttr("exception.message", "short msg")
        ]);

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.Equal("full stack", events[0].Exception);
    }

    // ── PropertiesJson ───────────────────────────────────────────────────────

    [Fact]
    public void Map_WellKnownKeysExcludedFromProperties()
    {
        // Arrange
        var request = BuildRequest(logAttrs:
        [
            StrAttr("correlation_id", "x"),
            StrAttr("exception.stacktrace", "y"),
            StrAttr("exception.message", "z"),
            StrAttr("custom.key", "value")
        ]);

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.NotNull(events[0].PropertiesJson);
        Assert.DoesNotContain("correlation_id", events[0].PropertiesJson!);
        Assert.DoesNotContain("exception.stacktrace", events[0].PropertiesJson!);
        Assert.DoesNotContain("exception.message", events[0].PropertiesJson!);
        Assert.Contains("custom.key", events[0].PropertiesJson!);
    }

    [Fact]
    public void Map_ServiceNameExcludedFromProperties()
    {
        // Arrange — service.name is set as a resource attribute
        var request = BuildRequest(serviceName: "svc");

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert — service.name must not appear in PropertiesJson
        if (events[0].PropertiesJson is not null)
            Assert.DoesNotContain("service.name", events[0].PropertiesJson!);
    }

    [Fact]
    public void Map_NoExtraAttributes_PropertiesJsonIsNull()
    {
        // Arrange — only service.name, no custom attrs
        var request = BuildRequest(serviceName: "svc");

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.Null(events[0].PropertiesJson);
    }

    [Fact]
    public void Map_AdditionalAttributes_StoredInPropertiesJson()
    {
        // Arrange
        var request = BuildRequest(resourceAttrs: [StrAttr("region", "eu-west-1")]);

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.NotNull(events[0].PropertiesJson);
        Assert.Contains("region", events[0].PropertiesJson!);
        Assert.Contains("eu-west-1", events[0].PropertiesJson!);
    }

    // ── Attribute value types ────────────────────────────────────────────────

    [Fact]
    public void Map_IntAttribute_ConvertedToString()
    {
        // Arrange
        var request = BuildRequest(logAttrs:
        [
            new KeyValue { Key = "count", Value = new AnyValue { IntValue = 42 } }
        ]);

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.Contains("42", events[0].PropertiesJson!);
    }

    [Fact]
    public void Map_BoolAttribute_ConvertedToString()
    {
        // Arrange
        var request = BuildRequest(logAttrs:
        [
            new KeyValue { Key = "flag", Value = new AnyValue { BoolValue = true } }
        ]);

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.Contains("flag", events[0].PropertiesJson!);
    }

    [Fact]
    public void Map_DoubleAttribute_ConvertedToString()
    {
        // Arrange
        var request = BuildRequest(logAttrs:
        [
            new KeyValue { Key = "latency", Value = new AnyValue { DoubleValue = 1.23 } }
        ]);

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.Contains("latency", events[0].PropertiesJson!);
    }

    [Fact]
    public void Map_UnknownAttributeValueType_Ignored()
    {
        // Arrange — KvlistValue is not handled, should be silently skipped
        var request = BuildRequest(logAttrs:
        [
            new KeyValue { Key = "nested", Value = new AnyValue { KvlistValue = new KeyValueList() } },
            StrAttr("kept", "yes")
        ]);

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.DoesNotContain("nested", events[0].PropertiesJson ?? "");
        Assert.Contains("kept", events[0].PropertiesJson!);
    }

    // ── Attribute merge precedence ────────────────────────────────────────────

    [Fact]
    public void Map_LogAttrOverridesScopeAttr()
    {
        // Arrange
        var request = BuildRequest(
            scopeAttrs: [StrAttr("env", "staging")],
            logAttrs:   [StrAttr("env", "production")]);

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.Contains("production", events[0].PropertiesJson!);
        Assert.DoesNotContain("staging", events[0].PropertiesJson!);
    }

    [Fact]
    public void Map_ScopeAttrOverridesResourceAttr()
    {
        // Arrange
        var request = BuildRequest(
            resourceAttrs: [StrAttr("env", "staging")],
            scopeAttrs:    [StrAttr("env", "production")]);

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.Contains("production", events[0].PropertiesJson!);
        Assert.DoesNotContain("staging", events[0].PropertiesJson!);
    }

    // ── MessageTemplate ──────────────────────────────────────────────────────

    [Fact]
    public void Map_OriginalFormatAttribute_MappedToMessageTemplate()
    {
        // Arrange
        var request = BuildRequest(logAttrs: [StrAttr("{OriginalFormat}", "Hello {Name}")]);

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.Equal("Hello {Name}", events[0].MessageTemplate);
    }

    [Fact]
    public void Map_OriginalFormatAttribute_ExcludedFromPropertiesJson()
    {
        // Arrange
        var request = BuildRequest(logAttrs: [StrAttr("{OriginalFormat}", "Hello {Name}"), StrAttr("Name", "World")]);

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.DoesNotContain("{OriginalFormat}", events[0].PropertiesJson ?? "");
        Assert.Contains("Name", events[0].PropertiesJson!);
    }

    [Fact]
    public void Map_NoOriginalFormat_MessageTemplateIsNull()
    {
        // Arrange
        var request = BuildRequest();

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.Null(events[0].MessageTemplate);
    }

    // ── Multiple records ─────────────────────────────────────────────────────

    [Fact]
    public void Map_MultipleResourceLogs_AllMapped()
    {
        // Arrange
        var request = new ExportLogsServiceRequest();
        for (var i = 0; i < 3; i++)
        {
            var rl = new ResourceLogs { Resource = new Resource() };
            rl.Resource.Attributes.Add(StrAttr("service.name", $"svc-{i}"));
            var sl = new ScopeLogs();
            var lr = new LogRecord
            {
                TimeUnixNano = 1_700_000_000_000_000_000UL,
                SeverityNumber = SeverityNumber.Info,
                Body = new AnyValue { StringValue = $"msg-{i}" }
            };
            sl.LogRecords.Add(lr);
            rl.ScopeLogs.Add(sl);
            request.ResourceLogs.Add(rl);
        }

        // Act
        var events = OtlpProtoMapper.Map(request);

        // Assert
        Assert.Equal(3, events.Count);
        Assert.Equal(["svc-0", "svc-1", "svc-2"], events.Select(e => e.Source));
    }
}
