using System.ComponentModel;
using System.Text.Json;
using Loggles.Core.DTOs;
using Loggles.Core.Interfaces;
using Loggles.Core.Models;
using ModelContextProtocol.Server;

namespace Loggles.Api.Mcp;

[McpServerToolType]
public sealed class LogTools
{
    private readonly ILogStore _store;

    public LogTools(ILogStore store)
    {
        _store = store;
    }

    [McpServerTool(Name = "search_logs"), Description("Search log events with optional filters.")]
    public async Task<object> SearchLogs(
        [Description("Start of time range (ISO 8601 UTC)")] string from,
        [Description("End of time range (ISO 8601 UTC)")] string to,
        [Description("Minimum log level (0=Trace..5=Critical)")] int? levelMin = null,
        [Description("Filter by source/service name")] string? source = null,
        [Description("Filter by correlation ID")] string? correlationId = null,
        [Description("Full-text search in message")] string? text = null,
        [Description("Filter by message template (partial match)")] string? messageTemplate = null,
        [Description("Filter by structured log properties as a JSON object, e.g. {\"userId\":\"42\",\"action\":\"start\"}")] string? properties = null,
        [Description("Maximum number of results")] int pageSize = 100,
        [Description("Pagination token from a previous search")] string? pageToken = null)
    {
        Dictionary<string, string>? parsedProperties = null;
        if (!string.IsNullOrEmpty(properties))
            parsedProperties = JsonSerializer.Deserialize<Dictionary<string, string>>(properties);

        var query = new SearchQuery
        {
            From = DateTime.Parse(from, null, System.Globalization.DateTimeStyles.RoundtripKind),
            To = DateTime.Parse(to, null, System.Globalization.DateTimeStyles.RoundtripKind),
            LevelMin = levelMin.HasValue ? (Loggles.Core.Models.LogLevel)levelMin.Value : null,
            Source = source,
            CorrelationId = correlationId,
            Text = text,
            MessageTemplate = messageTemplate,
            Properties = parsedProperties,
            PageSize = pageSize,
            PageToken = pageToken
        };

        var result = await _store.SearchAsync(query);
        return new { Items = ToResponse(result.Items), result.NextPageToken };
    }

    [McpServerTool(Name = "get_log_by_id"), Description("Retrieve a single log event by its ID.")]
    public async Task<object?> GetLogById([Description("The log event ID")] long id)
    {
        var log = await _store.GetByIdAsync(id);
        return log is null ? null : ToResponse(log);
    }

    [McpServerTool(Name = "get_services"), Description("List all distinct service/source names that have emitted logs.")]
    public async Task<object> GetServices()
    {
        return await _store.GetServicesAsync();
    }

    [McpServerTool(Name = "get_log_levels"), Description("List all distinct log levels present in the store.")]
    public async Task<object> GetLogLevels()
    {
        return await _store.GetLogLevelsAsync();
    }

    [McpServerTool(Name = "get_properties"), Description("List all distinct property keys present across log events.")]
    public async Task<object> GetProperties()
    {
        return await _store.GetPropertiesAsync();
    }

    [McpServerTool(Name = "get_log_stats"), Description("Get log counts grouped by level and source for a time window.")]
    public async Task<object> GetLogStats(
        [Description("Start of time range (ISO 8601 UTC)")] string from,
        [Description("End of time range (ISO 8601 UTC)")] string to,
        [Description("Filter by source/service name")] string? source = null)
    {
        return await _store.GetStatsAsync(
            DateTime.Parse(from, null, System.Globalization.DateTimeStyles.RoundtripKind),
            DateTime.Parse(to, null, System.Globalization.DateTimeStyles.RoundtripKind),
            source);
    }

    [McpServerTool(Name = "get_logs_by_trace_id"), Description("Retrieve all log events sharing a trace/correlation ID, ordered by time.")]
    public async Task<object> GetLogsByTraceId(
        [Description("The trace or correlation ID")] string traceId)
    {
        return ToResponse(await _store.GetLogsByTraceIdAsync(traceId));
    }

    [McpServerTool(Name = "get_related_logs"), Description("Get logs within a time window around a specific log event, useful for understanding context.")]
    public async Task<object> GetRelatedLogs(
        [Description("The anchor log event ID")] long id,
        [Description("Seconds before and after the anchor event to include (default 30)")] int windowSeconds = 30)
    {
        return ToResponse(await _store.GetRelatedLogsAsync(id, windowSeconds));
    }

    [McpServerTool(Name = "get_recent_errors"), Description("Get the most recent error and critical log events.")]
    public async Task<object> GetRecentErrors(
        [Description("Maximum number of results (default 50)")] int n = 50,
        [Description("Filter by source/service name")] string? source = null)
    {
        return ToResponse(await _store.GetRecentErrorsAsync(n, source));
    }

    [McpServerTool(Name = "tail_logs"), Description("Get the most recent log events across all levels (like tail -f snapshot). Use from/to to scope to a specific window, e.g. post-deployment verification.")]
    public async Task<object> TailLogs(
        [Description("Maximum number of results (default 50)")] int n = 50,
        [Description("Filter by source/service name")] string? source = null,
        [Description("Only include events at or after this time (ISO 8601 UTC)")] string? from = null,
        [Description("Only include events at or before this time (ISO 8601 UTC)")] string? to = null)
    {
        var fromDt = from is not null ? DateTime.Parse(from, null, System.Globalization.DateTimeStyles.RoundtripKind) : (DateTime?)null;
        var toDt = to is not null ? DateTime.Parse(to, null, System.Globalization.DateTimeStyles.RoundtripKind) : (DateTime?)null;
        return ToResponse(await _store.TailLogsAsync(n, source, fromDt, toDt));
    }

    [McpServerTool(Name = "get_log_rate"), Description("Get log counts bucketed by time interval to observe traffic patterns.")]
    public async Task<object> GetLogRate(
        [Description("Start of time range (ISO 8601 UTC)")] string from,
        [Description("End of time range (ISO 8601 UTC)")] string to,
        [Description("Bucket size in minutes (default 1)")] int bucketMinutes = 1,
        [Description("Filter by source/service name")] string? source = null,
        [Description("Minimum log level (0=Trace..5=Critical)")] int? levelMin = null)
    {
        return await _store.GetLogRateAsync(
            DateTime.Parse(from, null, System.Globalization.DateTimeStyles.RoundtripKind),
            DateTime.Parse(to, null, System.Globalization.DateTimeStyles.RoundtripKind),
            bucketMinutes, source, levelMin);
    }

    [McpServerTool(Name = "find_log_patterns"), Description("Cluster log messages by pattern to surface recurring errors or frequent message types.")]
    public async Task<object> FindLogPatterns(
        [Description("Start of time range (ISO 8601 UTC)")] string from,
        [Description("End of time range (ISO 8601 UTC)")] string to,
        [Description("Filter by source/service name")] string? source = null,
        [Description("Minimum log level (0=Trace..5=Critical)")] int? levelMin = null,
        [Description("Maximum number of top patterns to return (default 20)")] int topN = 20)
    {
        return await _store.FindLogPatternsAsync(
            DateTime.Parse(from, null, System.Globalization.DateTimeStyles.RoundtripKind),
            DateTime.Parse(to, null, System.Globalization.DateTimeStyles.RoundtripKind),
            source, levelMin, topN);
    }

    [McpServerTool(Name = "audit_log_quality"), Description("Audit logging hygiene: reports how many logs lack a message template or structured properties, broken down by source with samples. Call early in a debug session to surface instrumentation gaps.")]
    public async Task<object> AuditLogQuality(
        [Description("Start of time range (ISO 8601 UTC)")] string from,
        [Description("End of time range (ISO 8601 UTC)")] string to,
        [Description("Filter by source/service name")] string? source = null,
        [Description("Number of sample logs without a template to include (default 5)")] int sampleSize = 5)
    {
        return await _store.GetLogQualityReportAsync(
            DateTime.Parse(from, null, System.Globalization.DateTimeStyles.RoundtripKind),
            DateTime.Parse(to, null, System.Globalization.DateTimeStyles.RoundtripKind),
            source, sampleSize);
    }

    [McpServerTool(Name = "get_message_templates"), Description("List all distinct message templates. Use this to discover what event types exist before filtering with search_logs(messageTemplate: ...).")]
    public async Task<object> GetMessageTemplates(
        [Description("Filter by source/service name")] string? source = null)
    {
        return await _store.GetMessageTemplatesAsync(source);
    }

    [McpServerTool(Name = "get_property_values"), Description("List distinct values for a structured log property key. Use when you know a property exists but need to see what values to filter on.")]
    public async Task<object> GetPropertyValues(
        [Description("The property key to look up (e.g. 'userId', 'orderId')")] string key,
        [Description("Start of time range (ISO 8601 UTC)")] string from,
        [Description("End of time range (ISO 8601 UTC)")] string to,
        [Description("Filter by source/service name")] string? source = null,
        [Description("Maximum number of distinct values to return (default 50)")] int limit = 50)
    {
        return await _store.GetPropertyValuesAsync(key,
            DateTime.Parse(from, null, System.Globalization.DateTimeStyles.RoundtripKind),
            DateTime.Parse(to, null, System.Globalization.DateTimeStyles.RoundtripKind),
            source, limit);
    }

    private static object ToResponse(LogEvent e) => new
    {
        e.Id, e.Timestamp, e.Level, e.Source, e.LoggerName,
        e.CorrelationId, e.Message, e.MessageTemplate, e.Exception,
        Properties = e.PropertiesJson is not null
            ? JsonSerializer.Deserialize<Dictionary<string, object>>(e.PropertiesJson)
            : null
    };

    private static IEnumerable<object> ToResponse(IEnumerable<LogEvent> logs)
        => logs.Select(ToResponse);

    [McpServerTool(Name = "clear_logs"), Description("Delete all log events from the store. Use at the start of a new debugging session to clear logs from previous sessions.")]
    public async Task<string> ClearLogs()
    {
        await _store.ClearAllAsync();
        return "Log store cleared.";
    }

    [McpServerTool(Name = "get_error_spikes"), Description("Identify time buckets where error count exceeded a threshold — useful for detecting incidents.")]
    public async Task<object> GetErrorSpikes(
        [Description("Start of time range (ISO 8601 UTC)")] string from,
        [Description("End of time range (ISO 8601 UTC)")] string to,
        [Description("Minimum error count per bucket to report (default 10)")] int threshold = 10,
        [Description("Bucket size in minutes (default 5)")] int bucketMinutes = 5)
    {
        return await _store.GetErrorSpikesAsync(
            DateTime.Parse(from, null, System.Globalization.DateTimeStyles.RoundtripKind),
            DateTime.Parse(to, null, System.Globalization.DateTimeStyles.RoundtripKind),
            threshold, bucketMinutes);
    }
}
