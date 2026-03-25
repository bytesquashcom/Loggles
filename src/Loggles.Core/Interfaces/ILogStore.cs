using Loggles.Core.DTOs;
using Loggles.Core.Models;


namespace Loggles.Core.Interfaces;

public interface ILogStore
{
    Task WriteAsync(IReadOnlyList<LogEvent> events, CancellationToken ct = default);
    Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken ct = default);
    Task<LogEvent?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<Dictionary<string, int>> GetLevelCountsAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct = default);

    Task<List<string>> GetServicesAsync(CancellationToken ct = default);
    Task<List<string>> GetPropertiesAsync(CancellationToken ct = default);
    Task<List<string>> GetLogLevelsAsync(CancellationToken ct = default);
    Task<LogStats> GetStatsAsync(DateTime from, DateTime to, string? source = null, CancellationToken ct = default);
    Task<List<LogEvent>> GetLogsByTraceIdAsync(string traceId, CancellationToken ct = default);
    Task<List<LogEvent>> GetRelatedLogsAsync(long id, int windowSeconds = 30, CancellationToken ct = default);
    Task<List<LogEvent>> GetRecentErrorsAsync(int n = 50, string? source = null, CancellationToken ct = default);
    Task<List<LogEvent>> TailLogsAsync(int n = 50, string? source = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
    Task<List<LogRateBucket>> GetLogRateAsync(DateTime from, DateTime to, int bucketMinutes = 1, string? source = null, int? levelMin = null, CancellationToken ct = default);
    Task<List<LogPattern>> FindLogPatternsAsync(DateTime from, DateTime to, string? source = null, int? levelMin = null, int topN = 20, CancellationToken ct = default);
    Task<List<ErrorSpike>> GetErrorSpikesAsync(DateTime from, DateTime to, int threshold = 10, int bucketMinutes = 5, CancellationToken ct = default);

    Task<LogQualityReport> GetLogQualityReportAsync(DateTime from, DateTime to, string? source = null, int sampleSize = 5, CancellationToken ct = default);
    Task<List<string>> GetMessageTemplatesAsync(string? source = null, CancellationToken ct = default);
    Task<List<string>> GetPropertyValuesAsync(string key, DateTime from, DateTime to, string? source = null, int limit = 50, CancellationToken ct = default);

    /// <summary>Deletes all log events from the store.</summary>
    Task ClearAllAsync(CancellationToken ct = default);
}
