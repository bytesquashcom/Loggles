using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dapper;
using Loggles.Core.DTOs;
using Loggles.Core.Interfaces;
using Loggles.Core.Models;
using Microsoft.Data.Sqlite;

namespace Loggles.Infrastructure.Persistence;

public sealed class SqliteLogStore : ILogStore
{
    private readonly string _connectionString;

    public SqliteLogStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task WriteAsync(IReadOnlyList<LogEvent> events, CancellationToken ct = default)
    {
        if (events.Count == 0) return;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        foreach (var e in events)
        {
            await conn.ExecuteAsync("""
                INSERT OR IGNORE INTO logs (timestamp, level, source, logger_name, correlation_id, message, message_template, exception, properties_json, fingerprint)
                VALUES (@Timestamp, @Level, @Source, @LoggerName, @CorrelationId, @Message, @MessageTemplate, @Exception, @PropertiesJson, @Fingerprint);
                """,
                new
                {
                    Timestamp = e.Timestamp.ToString("o"),
                    Level = (int)e.Level,
                    e.Source,
                    e.LoggerName,
                    e.CorrelationId,
                    e.Message,
                    e.MessageTemplate,
                    e.Exception,
                    e.PropertiesJson,
                    Fingerprint = ComputeFingerprint(e)
                },
                transaction: (SqliteTransaction)tx);
        }

        await tx.CommitAsync(ct);
    }

    public async Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = new StringBuilder("SELECT id, timestamp, level, source, logger_name, correlation_id, message, message_template, exception, properties_json FROM logs WHERE 1=1");
        var p = new DynamicParameters();

        sql.Append(" AND timestamp >= @From");
        p.Add("From", query.From.ToString("o"));

        sql.Append(" AND timestamp <= @To");
        p.Add("To", query.To.ToString("o"));

        if (query.LevelMin.HasValue)
        {
            sql.Append(" AND level >= @LevelMin");
            p.Add("LevelMin", (int)query.LevelMin.Value);
        }

        if (!string.IsNullOrEmpty(query.Source))
        {
            sql.Append(" AND source = @Source");
            p.Add("Source", query.Source);
        }

        if (!string.IsNullOrEmpty(query.LoggerName))
        {
            sql.Append(" AND logger_name = @LoggerName");
            p.Add("LoggerName", query.LoggerName);
        }

        if (!string.IsNullOrEmpty(query.CorrelationId))
        {
            sql.Append(" AND correlation_id = @CorrelationId");
            p.Add("CorrelationId", query.CorrelationId);
        }

        if (!string.IsNullOrEmpty(query.Text))
        {
            sql.Append(" AND message LIKE @Text");
            p.Add("Text", $"%{query.Text}%");
        }

        if (!string.IsNullOrEmpty(query.MessageTemplate))
        {
            sql.Append(" AND message_template LIKE @MessageTemplate");
            p.Add("MessageTemplate", $"%{query.MessageTemplate}%");
        }

        if (query.Properties is { Count: > 0 })
        {
            int i = 0;
            foreach (var (key, value) in query.Properties)
            {
                var paramName = $"PropVal{i}";
                sql.Append($" AND CAST(json_extract(properties_json, '$.{key}') AS TEXT) = @{paramName}");
                p.Add(paramName, value);
                i++;
            }
        }

        int offset = 0;
        if (!string.IsNullOrEmpty(query.PageToken) && int.TryParse(query.PageToken, out var parsedOffset))
            offset = parsedOffset;

        sql.Append(" ORDER BY timestamp DESC LIMIT @Limit OFFSET @Offset");
        p.Add("Limit", query.PageSize + 1);
        p.Add("Offset", offset);

        var rows = (await conn.QueryAsync<dynamic>(sql.ToString(), p)).ToList();

        var hasMore = rows.Count > query.PageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var items = rows.Select(MapRow).ToList();
        var nextToken = hasMore ? (offset + query.PageSize).ToString() : null;

        return new SearchResult { Items = items, NextPageToken = nextToken };
    }

    public async Task<LogEvent?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var row = await conn.QuerySingleOrDefaultAsync<dynamic>(
            "SELECT id, timestamp, level, source, logger_name, correlation_id, message, message_template, exception, properties_json FROM logs WHERE id = @Id",
            new { Id = id });

        return row is null ? null : MapRow(row);
    }

    public async Task<Dictionary<string, int>> GetLevelCountsAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var rows = await conn.QueryAsync<dynamic>(
            "SELECT level, COUNT(*) as count FROM logs WHERE timestamp >= @From AND timestamp <= @To GROUP BY level",
            new { From = from.ToString("o"), To = to.ToString("o") });

        return rows.ToDictionary(
            r => ((Core.Models.LogLevel)(int)r.level).ToString(),
            r => (int)r.count);
    }

    public async Task DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync("DELETE FROM logs WHERE timestamp < @Cutoff", new { Cutoff = cutoff.ToString("o") });
    }

    public async Task<List<string>> GetServicesAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<string>("SELECT DISTINCT source FROM logs ORDER BY source");
        return rows.ToList();
    }

    public async Task<List<string>> GetPropertiesAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<string>(
            "SELECT DISTINCT je.key FROM logs, json_each(logs.properties_json) AS je WHERE logs.properties_json IS NOT NULL ORDER BY je.key");
        return rows.ToList();
    }

    public async Task<List<string>> GetLogLevelsAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<int>("SELECT DISTINCT level FROM logs ORDER BY level");
        return rows.Select(l => ((Core.Models.LogLevel)l).ToString()).ToList();
    }

    public async Task<LogStats> GetStatsAsync(DateTime from, DateTime to, string? source = null, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var whereSource = string.IsNullOrEmpty(source) ? "" : " AND source = @Source";
        var p = new { From = from.ToString("o"), To = to.ToString("o"), Source = source };

        var levelRows = await conn.QueryAsync<dynamic>(
            $"SELECT level, COUNT(*) as count FROM logs WHERE timestamp >= @From AND timestamp <= @To{whereSource} GROUP BY level",
            p);

        var sourceRows = await conn.QueryAsync<dynamic>(
            $"SELECT source, COUNT(*) as count FROM logs WHERE timestamp >= @From AND timestamp <= @To{whereSource} GROUP BY source ORDER BY count DESC",
            p);

        var byLevel = levelRows.ToDictionary(
            r => ((Core.Models.LogLevel)(int)r.level).ToString(),
            r => (int)r.count);

        var bySource = sourceRows.ToDictionary(
            r => (string)r.source,
            r => (int)r.count);

        return new LogStats
        {
            TotalCount = byLevel.Values.Sum(),
            ByLevel = byLevel,
            BySource = bySource
        };
    }

    public async Task<List<LogEvent>> GetLogsByTraceIdAsync(string traceId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<dynamic>(
            "SELECT id, timestamp, level, source, logger_name, correlation_id, message, message_template, exception, properties_json FROM logs WHERE correlation_id = @TraceId ORDER BY timestamp ASC",
            new { TraceId = traceId });
        return rows.Select(MapRow).ToList();
    }

    public async Task<List<LogEvent>> GetRelatedLogsAsync(long id, int windowSeconds = 30, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var pivotStr = await conn.QuerySingleOrDefaultAsync<string>(
            "SELECT timestamp FROM logs WHERE id = @Id", new { Id = id });
        if (pivotStr is null) return [];

        var pivot = DateTime.Parse(pivotStr, null, System.Globalization.DateTimeStyles.RoundtripKind);
        var from = pivot.AddSeconds(-windowSeconds).ToString("o");
        var to   = pivot.AddSeconds(+windowSeconds).ToString("o");

        var rows = await conn.QueryAsync<dynamic>(
            """
            SELECT id, timestamp, level, source, logger_name, correlation_id, message, exception, properties_json
            FROM logs
            WHERE timestamp >= @From
              AND timestamp <= @To
              AND id != @Id
            ORDER BY timestamp ASC
            """,
            new { From = from, To = to, Id = id });

        return rows.Select(MapRow).ToList();
    }

    public async Task<List<LogEvent>> GetRecentErrorsAsync(int n = 50, string? source = null, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var whereSource = string.IsNullOrEmpty(source) ? "" : " AND source = @Source";
        var rows = await conn.QueryAsync<dynamic>(
            $"SELECT id, timestamp, level, source, logger_name, correlation_id, message, message_template, exception, properties_json FROM logs WHERE level >= 4{whereSource} ORDER BY timestamp DESC LIMIT @N",
            new { N = n, Source = source });

        return rows.Select(MapRow).ToList();
    }

    public async Task<List<LogEvent>> TailLogsAsync(int n = 50, string? source = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var conditions = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(source)) conditions.Add("source = @Source");
        if (from.HasValue) conditions.Add("timestamp >= @From");
        if (to.HasValue) conditions.Add("timestamp <= @To");
        var where = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";

        var rows = await conn.QueryAsync<dynamic>(
            $"SELECT id, timestamp, level, source, logger_name, correlation_id, message, message_template, exception, properties_json FROM logs{where} ORDER BY timestamp DESC LIMIT @N",
            new { N = n, Source = source, From = from?.ToString("o"), To = to?.ToString("o") });

        return rows.Select(MapRow).ToList();
    }

    public async Task<List<LogRateBucket>> GetLogRateAsync(DateTime from, DateTime to, int bucketMinutes = 1, string? source = null, int? levelMin = null, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var conditions = new StringBuilder("timestamp >= @From AND timestamp <= @To");
        if (!string.IsNullOrEmpty(source)) conditions.Append(" AND source = @Source");
        if (levelMin.HasValue) conditions.Append(" AND level >= @LevelMin");

        var bucketSeconds = bucketMinutes * 60;
        var sql = $"""
            SELECT (CAST(strftime('%s', timestamp) AS INTEGER) / @BucketSeconds) * @BucketSeconds AS bucket_epoch,
                   COUNT(*) AS count
            FROM logs
            WHERE {conditions}
            GROUP BY bucket_epoch
            ORDER BY bucket_epoch
            """;

        var rows = await conn.QueryAsync<dynamic>(sql, new { From = from.ToString("o"), To = to.ToString("o"), Source = source, LevelMin = levelMin, BucketSeconds = bucketSeconds });

        return rows.Select(r => new LogRateBucket
        {
            BucketStart = DateTimeOffset.FromUnixTimeSeconds((long)r.bucket_epoch).UtcDateTime,
            Count = (int)r.count
        }).ToList();
    }

    public async Task<List<LogPattern>> FindLogPatternsAsync(DateTime from, DateTime to, string? source = null, int? levelMin = null, int topN = 20, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var conditions = new StringBuilder("timestamp >= @From AND timestamp <= @To");
        if (!string.IsNullOrEmpty(source)) conditions.Append(" AND source = @Source");
        if (levelMin.HasValue) conditions.Append(" AND level >= @LevelMin");

        var rows = (await conn.QueryAsync<dynamic>(
            $"SELECT message, level FROM logs WHERE {conditions}",
            new { From = from.ToString("o"), To = to.ToString("o"), Source = source, LevelMin = levelMin })).ToList();

        var groups = rows
            .GroupBy(r => NormalizeMessage((string)r.message))
            .OrderByDescending(g => g.Count())
            .Take(topN);

        return groups.Select(g => new LogPattern
        {
            Pattern = g.Key,
            Count = g.Count(),
            SampleMessage = (string)g.First().message,
            Level = ((Core.Models.LogLevel)(int)g.First().level).ToString()
        }).ToList();
    }

    public async Task<List<ErrorSpike>> GetErrorSpikesAsync(DateTime from, DateTime to, int threshold = 10, int bucketMinutes = 5, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var bucketSeconds = bucketMinutes * 60;
        var rows = await conn.QueryAsync<dynamic>(
            """
            SELECT (CAST(strftime('%s', timestamp) AS INTEGER) / @BucketSeconds) * @BucketSeconds AS bucket_epoch,
                   SUM(CASE WHEN level >= 4 THEN 1 ELSE 0 END) AS error_count,
                   COUNT(*) AS total_count
            FROM logs
            WHERE timestamp >= @From AND timestamp <= @To
            GROUP BY bucket_epoch
            HAVING error_count >= @Threshold
            ORDER BY bucket_epoch
            """,
            new { From = from.ToString("o"), To = to.ToString("o"), BucketSeconds = bucketSeconds, Threshold = threshold });

        return rows.Select(r => new ErrorSpike
        {
            BucketStart = DateTimeOffset.FromUnixTimeSeconds((long)r.bucket_epoch).UtcDateTime,
            ErrorCount = (int)r.error_count,
            TotalCount = (int)r.total_count
        }).ToList();
    }

    public async Task<LogQualityReport> GetLogQualityReportAsync(DateTime from, DateTime to, string? source = null, int sampleSize = 5, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var fromStr = from.ToString("o");
        var toStr = to.ToString("o");
        var whereBase = string.IsNullOrEmpty(source)
            ? "WHERE timestamp >= @From AND timestamp <= @To"
            : "WHERE timestamp >= @From AND timestamp <= @To AND source = @Source";
        var p = new { From = fromStr, To = toStr, Source = source, SampleSize = sampleSize };

        var totals = await conn.QuerySingleAsync<dynamic>(
            $"""
            SELECT COUNT(*) AS total,
                   COALESCE(SUM(CASE WHEN message_template IS NULL THEN 1 ELSE 0 END), 0) AS no_tmpl,
                   COALESCE(SUM(CASE WHEN properties_json IS NULL THEN 1 ELSE 0 END), 0) AS no_props
            FROM logs {whereBase}
            """, p);

        int total = (int)totals.total;
        int noTmpl = (int)totals.no_tmpl;
        int noProps = (int)totals.no_props;

        var sourceRows = await conn.QueryAsync<dynamic>(
            $"""
            SELECT source,
                   COUNT(*) AS total,
                   SUM(CASE WHEN message_template IS NULL THEN 1 ELSE 0 END) AS no_tmpl,
                   SUM(CASE WHEN properties_json IS NULL THEN 1 ELSE 0 END) AS no_props
            FROM logs {whereBase}
            GROUP BY source
            ORDER BY total DESC
            """, p);

        var sampleRows = await conn.QueryAsync<dynamic>(
            $"""
            SELECT id, source, level, message
            FROM logs {whereBase} AND message_template IS NULL
            ORDER BY timestamp DESC
            LIMIT @SampleSize
            """, p);

        return new LogQualityReport
        {
            TotalCount = total,
            WithoutTemplate = new QualityCounter { Count = noTmpl, Pct = total == 0 ? 0 : Math.Round((double)noTmpl / total * 100, 1) },
            WithoutProperties = new QualityCounter { Count = noProps, Pct = total == 0 ? 0 : Math.Round((double)noProps / total * 100, 1) },
            BySource = sourceRows.Select(r => new SourceQuality
            {
                Source = (string)r.source,
                Total = (int)r.total,
                WithoutTemplate = (int)r.no_tmpl,
                WithoutProperties = (int)r.no_props
            }).ToList(),
            SampleLogsWithoutTemplate = sampleRows.Select(r => new SampleLog
            {
                Id = (long)r.id,
                Source = (string)r.source,
                Level = ((Core.Models.LogLevel)(int)r.level).ToString(),
                Message = (string)r.message
            }).ToList()
        };
    }

    public async Task<List<string>> GetMessageTemplatesAsync(string? source = null, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var whereSource = string.IsNullOrEmpty(source) ? "" : " AND source = @Source";
        var rows = await conn.QueryAsync<string>(
            $"SELECT DISTINCT message_template FROM logs WHERE message_template IS NOT NULL{whereSource} ORDER BY message_template",
            new { Source = source });

        return rows.ToList();
    }

    public async Task<List<string>> GetPropertyValuesAsync(string key, DateTime from, DateTime to, string? source = null, int limit = 50, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var whereSource = string.IsNullOrEmpty(source) ? "" : " AND source = @Source";
        var rows = await conn.QueryAsync<string>(
            $"""
            SELECT DISTINCT CAST(json_extract(properties_json, '$.{key}') AS TEXT) AS val
            FROM logs
            WHERE properties_json IS NOT NULL
              AND json_extract(properties_json, '$.{key}') IS NOT NULL
              AND timestamp >= @From AND timestamp <= @To{whereSource}
            ORDER BY val
            LIMIT @Limit
            """,
            new { From = from.ToString("o"), To = to.ToString("o"), Source = source, Limit = limit });

        return rows.ToList();
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync("DELETE FROM logs");
    }

    internal static string ComputeFingerprint(LogEvent e)
    {
        var raw = $"{e.Timestamp:o}|{(int)e.Level}|{e.Source}|{e.CorrelationId}|{e.Message}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }

    private static readonly Regex GuidPattern = new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);
    private static readonly Regex NumberPattern = new(@"\b\d+\b", RegexOptions.Compiled);
    private static readonly Regex HexPattern = new(@"0x[0-9a-fA-F]+", RegexOptions.Compiled);

    private static string NormalizeMessage(string message)
    {
        var s = GuidPattern.Replace(message, "{guid}");
        s = HexPattern.Replace(s, "{hex}");
        s = NumberPattern.Replace(s, "{n}");
        return s;
    }

    private static LogEvent MapRow(dynamic r) => new()
    {
        Id = (long)r.id,
        Timestamp = DateTime.Parse((string)r.timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind),
        Level = (Core.Models.LogLevel)(int)r.level,
        Source = (string)r.source,
        LoggerName = (string?)r.logger_name,
        CorrelationId = (string?)r.correlation_id,
        Message = (string)r.message,
        MessageTemplate = (string?)r.message_template,
        Exception = (string?)r.exception,
        PropertiesJson = (string?)r.properties_json
    };
}
