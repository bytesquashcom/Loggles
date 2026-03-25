using System.Text.Json;
using Loggles.Core.Models;
using Loggles.Core.Protos.Collector.Logs.V1;
using Loggles.Core.Protos.Common.V1;

namespace Loggles.Core.Services;

/// <summary>
/// Maps an OTLP protobuf <see cref="ExportLogsServiceRequest"/> to a list of <see cref="LogEvent"/>.
/// </summary>
public static class OtlpProtoMapper
{
    public static IReadOnlyList<LogEvent> Map(ExportLogsServiceRequest request)
    {
        var results = new List<LogEvent>();

        foreach (var rl in request.ResourceLogs)
        {
            var resourceAttrs = ExtractAttributes(rl.Resource?.Attributes);

            foreach (var sl in rl.ScopeLogs)
            {
                var scopeAttrs = ExtractAttributes(sl.Scope?.Attributes);
                var loggerName = string.IsNullOrEmpty(sl.Scope?.Name) ? null : sl.Scope.Name;

                foreach (var lr in sl.LogRecords)
                {
                    var logAttrs = ExtractAttributes(lr.Attributes);

                    var allAttrs = new Dictionary<string, string>(resourceAttrs);
                    foreach (var (k, v) in scopeAttrs) allAttrs[k] = v;
                    foreach (var (k, v) in logAttrs) allAttrs[k] = v;

                    var timestamp = ParseTimestamp(lr.TimeUnixNano != 0 ? lr.TimeUnixNano : lr.ObservedTimeUnixNano);
                    var level = MapSeverityToLevel((int)lr.SeverityNumber);
                    var message = lr.Body?.StringValue ?? string.Empty;

                    allAttrs.TryGetValue("service.name", out var source);
                    allAttrs.TryGetValue("correlation_id", out var correlationId);
                    allAttrs.TryGetValue("exception.stacktrace", out var exception);
                    if (exception is null) allAttrs.TryGetValue("exception.message", out exception);

                    var propsDict = new Dictionary<string, string>(allAttrs);
                    propsDict.Remove("service.name");
                    propsDict.Remove("correlation_id");
                    propsDict.Remove("exception.stacktrace");
                    propsDict.Remove("exception.message");

                    // Strip static OTel SDK metadata — constant for the process lifetime
                    propsDict.Remove("telemetry.sdk.name");
                    propsDict.Remove("telemetry.sdk.language");
                    propsDict.Remove("telemetry.sdk.version");
                    propsDict.Remove("service.instance.id"); // captured in LogEvent.Source

                    // Strip no-op root span parent (all zeros = no real parent)
                    if (propsDict.TryGetValue("ParentId", out var parentId) && parentId == "0000000000000000")
                        propsDict.Remove("ParentId");

                    propsDict.TryGetValue("{OriginalFormat}", out var messageTemplate);
                    propsDict.Remove("{OriginalFormat}");

                    results.Add(new LogEvent
                    {
                        Timestamp = timestamp,
                        Level = level,
                        Source = source ?? "unknown",
                        LoggerName = loggerName,
                        CorrelationId = correlationId,
                        Message = message,
                        MessageTemplate = messageTemplate,
                        Exception = exception,
                        PropertiesJson = propsDict.Count > 0 ? JsonSerializer.Serialize(propsDict) : null
                    });
                }
            }
        }

        return results;
    }

    private static Dictionary<string, string> ExtractAttributes(IEnumerable<KeyValue>? attrs)
    {
        var dict = new Dictionary<string, string>();
        if (attrs is null) return dict;

        foreach (var kv in attrs)
        {
            var strVal = kv.Value?.ValueCase switch
            {
                AnyValue.ValueOneofCase.StringValue => kv.Value.StringValue,
                AnyValue.ValueOneofCase.IntValue    => kv.Value.IntValue.ToString(),
                AnyValue.ValueOneofCase.BoolValue   => kv.Value.BoolValue.ToString(),
                AnyValue.ValueOneofCase.DoubleValue => kv.Value.DoubleValue.ToString(),
                _                                   => null
            };

            if (strVal is not null) dict[kv.Key] = strVal;
        }

        return dict;
    }

    private static DateTime ParseTimestamp(ulong timeUnixNano)
    {
        if (timeUnixNano == 0) return DateTime.UtcNow;
        var ticks = (long)(timeUnixNano / 100);
        return DateTime.UnixEpoch.AddTicks(ticks);
    }

    private static Models.LogLevel MapSeverityToLevel(int severityNumber) => severityNumber switch
    {
        >= 1 and <= 4   => Models.LogLevel.Trace,
        >= 5 and <= 8   => Models.LogLevel.Debug,
        >= 9 and <= 12  => Models.LogLevel.Information,
        >= 13 and <= 16 => Models.LogLevel.Warning,
        >= 17 and <= 20 => Models.LogLevel.Error,
        >= 21 and <= 24 => Models.LogLevel.Critical,
        _               => Models.LogLevel.Information
    };
}
