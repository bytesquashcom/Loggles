using System.Text.Json;
using System.Text.Json.Nodes;
using Loggles.Core.Models;

namespace Loggles.Core.Services;

/// <summary>
/// Maps OTLP/HTTP JSON (ResourceLogs[]) to a list of <see cref="LogEvent"/>.
/// </summary>
public static class OtlpMapper
{
    public static IReadOnlyList<LogEvent> Map(JsonNode root)
    {
        var results = new List<LogEvent>();
        var resourceLogs = root["resourceLogs"]?.AsArray() ?? root["resource_logs"]?.AsArray();
        if (resourceLogs is null) return results;

        foreach (var rl in resourceLogs)
        {
            if (rl is null) continue;
            var resourceAttrs = ExtractAttributes(rl["resource"]?["attributes"]?.AsArray());

            var scopeLogs = rl["scopeLogs"]?.AsArray() ?? rl["scope_logs"]?.AsArray();
            if (scopeLogs is null) continue;

            foreach (var sl in scopeLogs)
            {
                if (sl is null) continue;
                var scopeAttrs = ExtractAttributes(sl["scope"]?["attributes"]?.AsArray());
                var loggerName = sl["scope"]?["name"]?.GetValue<string>();

                var logRecords = sl["logRecords"]?.AsArray() ?? sl["log_records"]?.AsArray();
                if (logRecords is null) continue;

                foreach (var lr in logRecords)
                {
                    if (lr is null) continue;
                    var logAttrs = ExtractAttributes(lr["attributes"]?.AsArray());

                    // Merge all attributes (resource < scope < log)
                    var allAttrs = new Dictionary<string, string>(resourceAttrs);
                    foreach (var (k, v) in scopeAttrs) allAttrs[k] = v;
                    foreach (var (k, v) in logAttrs) allAttrs[k] = v;

                    var timestamp = ParseTimestamp(lr["timeUnixNano"]?.GetValue<string>()
                        ?? lr["observedTimeUnixNano"]?.GetValue<string>());

                    var severityNumber = lr["severityNumber"]?.GetValue<int>() ?? 0;
                    var level = MapSeverityToLevel(severityNumber);

                    var message = lr["body"]?["stringValue"]?.GetValue<string>()
                        ?? lr["body"]?["string_value"]?.GetValue<string>()
                        ?? string.Empty;

                    allAttrs.TryGetValue("service.name", out var source);
                    allAttrs.TryGetValue("correlation_id", out var correlationId);
                    allAttrs.TryGetValue("exception.stacktrace", out var exception);
                    if (exception is null) allAttrs.TryGetValue("exception.message", out exception);

                    // Remove well-known keys before storing as PropertiesJson
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

                    string? propsJson = propsDict.Count > 0
                        ? JsonSerializer.Serialize(propsDict)
                        : null;

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
                        PropertiesJson = propsJson
                    });
                }
            }
        }

        return results;
    }

    private static Dictionary<string, string> ExtractAttributes(JsonArray? attrs)
    {
        var dict = new Dictionary<string, string>();
        if (attrs is null) return dict;

        foreach (var attr in attrs)
        {
            if (attr is null) continue;
            var key = attr["key"]?.GetValue<string>();
            if (key is null) continue;

            var value = attr["value"];
            var strVal = value?["stringValue"]?.GetValue<string>()
                ?? value?["string_value"]?.GetValue<string>()
                ?? value?["intValue"]?.GetValue<long>().ToString()
                ?? value?["int_value"]?.GetValue<long>().ToString()
                ?? value?["boolValue"]?.GetValue<bool>().ToString()
                ?? value?["bool_value"]?.GetValue<bool>().ToString()
                ?? value?.ToJsonString();

            if (strVal is not null) dict[key] = strVal;
        }

        return dict;
    }

    private static DateTime ParseTimestamp(string? nanoStr)
    {
        if (string.IsNullOrEmpty(nanoStr) || !long.TryParse(nanoStr, out var nanos))
            return DateTime.UtcNow;

        var ticks = nanos / 100; // nanoseconds to ticks (1 tick = 100 ns)
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    private static Models.LogLevel MapSeverityToLevel(int severityNumber) => severityNumber switch
    {
        >= 1 and <= 4 => Models.LogLevel.Trace,
        >= 5 and <= 8 => Models.LogLevel.Debug,
        >= 9 and <= 12 => Models.LogLevel.Information,
        >= 13 and <= 16 => Models.LogLevel.Warning,
        >= 17 and <= 20 => Models.LogLevel.Error,
        >= 21 and <= 24 => Models.LogLevel.Critical,
        _ => Models.LogLevel.Information
    };
}
