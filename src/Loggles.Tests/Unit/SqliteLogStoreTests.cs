using Loggles.Core.DTOs;
using Loggles.Core.Models;
using Loggles.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Loggles.Tests.Unit;

public sealed class SqliteLogStoreTests : IAsyncLifetime
{
    private readonly string _connectionString;
    private SqliteLogStore _store = null!;

    // Shared-cache in-memory SQLite: no file I/O, unique name per test class instance
    private readonly SqliteConnection _keepAlive;

    public SqliteLogStoreTests()
    {
        var dbName = Guid.NewGuid().ToString("N");
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        // Keep a connection open so the in-memory DB persists for the lifetime of this test class
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    public async Task InitializeAsync()
    {
        await DbInitializer.InitAsync(_connectionString);
        _store = new SqliteLogStore(_connectionString);
    }

    public async Task DisposeAsync()
    {
        await _keepAlive.DisposeAsync();
    }

    private static LogEvent MakeEvent(
        LogLevel level = LogLevel.Information,
        string source = "test-svc",
        string message = "hello",
        string? correlationId = null,
        DateTime? timestamp = null) => new()
    {
        Timestamp = timestamp ?? DateTime.UtcNow,
        Level = level,
        Source = source,
        CorrelationId = correlationId,
        Message = message
    };

    [Fact]
    public async Task WriteAsync_ThenSearchAsync_ReturnsWrittenEvents()
    {
        // Arrange
        var events = new[] { MakeEvent(message: "written event") };

        // Act
        await _store.WriteAsync(events);
        var result = await _store.SearchAsync(new SearchQuery
        {
            From = DateTime.UtcNow.AddMinutes(-1),
            To = DateTime.UtcNow.AddMinutes(1)
        });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("written event", result.Items[0].Message);
    }

    [Fact]
    public async Task SearchAsync_FilterByLevel_OnlyReturnsMatchingLevel()
    {
        // Arrange
        await _store.WriteAsync([
            MakeEvent(level: LogLevel.Debug, message: "debug"),
            MakeEvent(level: LogLevel.Error, message: "error")
        ]);

        // Act
        var result = await _store.SearchAsync(new SearchQuery
        {
            From = DateTime.UtcNow.AddMinutes(-1),
            To = DateTime.UtcNow.AddMinutes(1),
            LevelMin = LogLevel.Error
        });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("error", result.Items[0].Message);
    }

    [Fact]
    public async Task SearchAsync_FilterBySource_OnlyReturnsMatchingSource()
    {
        // Arrange
        await _store.WriteAsync([
            MakeEvent(source: "svc-a", message: "from a"),
            MakeEvent(source: "svc-b", message: "from b")
        ]);

        // Act
        var result = await _store.SearchAsync(new SearchQuery
        {
            From = DateTime.UtcNow.AddMinutes(-1),
            To = DateTime.UtcNow.AddMinutes(1),
            Source = "svc-a"
        });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("from a", result.Items[0].Message);
    }

    [Fact]
    public async Task SearchAsync_FilterByCorrelationId_OnlyReturnsMatching()
    {
        // Arrange
        await _store.WriteAsync([
            MakeEvent(correlationId: "trace-1", message: "correlated"),
            MakeEvent(correlationId: null, message: "uncorrelated")
        ]);

        // Act
        var result = await _store.SearchAsync(new SearchQuery
        {
            From = DateTime.UtcNow.AddMinutes(-1),
            To = DateTime.UtcNow.AddMinutes(1),
            CorrelationId = "trace-1"
        });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("correlated", result.Items[0].Message);
    }

    [Fact]
    public async Task SearchAsync_TextSearch_OnlyReturnsContainingText()
    {
        // Arrange
        await _store.WriteAsync([
            MakeEvent(message: "connection timeout error"),
            MakeEvent(message: "user logged in")
        ]);

        // Act
        var result = await _store.SearchAsync(new SearchQuery
        {
            From = DateTime.UtcNow.AddMinutes(-1),
            To = DateTime.UtcNow.AddMinutes(1),
            Text = "timeout"
        });

        // Assert
        Assert.Single(result.Items);
        Assert.Contains("timeout", result.Items[0].Message);
    }

    [Fact]
    public async Task SearchAsync_Pagination_ReturnsNextPageToken()
    {
        // Arrange
        var events = Enumerable.Range(1, 5).Select(i => MakeEvent(message: $"event {i}")).ToArray();
        await _store.WriteAsync(events);

        // Act — page 1
        var page1 = await _store.SearchAsync(new SearchQuery
        {
            From = DateTime.UtcNow.AddMinutes(-1),
            To = DateTime.UtcNow.AddMinutes(1),
            PageSize = 3
        });

        // Assert
        Assert.Equal(3, page1.Items.Count);
        Assert.NotNull(page1.NextPageToken);

        // Act — page 2
        var page2 = await _store.SearchAsync(new SearchQuery
        {
            From = DateTime.UtcNow.AddMinutes(-1),
            To = DateTime.UtcNow.AddMinutes(1),
            PageSize = 3,
            PageToken = page1.NextPageToken
        });

        Assert.Equal(2, page2.Items.Count);
        Assert.Null(page2.NextPageToken);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsEvent()
    {
        // Arrange
        await _store.WriteAsync([MakeEvent(message: "findme")]);
        var all = await _store.SearchAsync(new SearchQuery
        {
            From = DateTime.UtcNow.AddMinutes(-1),
            To = DateTime.UtcNow.AddMinutes(1)
        });
        var id = all.Items[0].Id;

        // Act
        var evt = await _store.GetByIdAsync(id);

        // Assert
        Assert.NotNull(evt);
        Assert.Equal("findme", evt!.Message);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentId_ReturnsNull()
    {
        // Act
        var evt = await _store.GetByIdAsync(999_999);

        // Assert
        Assert.Null(evt);
    }

    [Fact]
    public async Task DeleteOlderThanAsync_DeletesOldEvents()
    {
        // Arrange
        var old = MakeEvent(message: "old", timestamp: DateTime.UtcNow.AddHours(-3));
        var recent = MakeEvent(message: "recent");
        await _store.WriteAsync([old, recent]);

        // Act
        await _store.DeleteOlderThanAsync(DateTime.UtcNow.AddHours(-1));

        // Assert
        var result = await _store.SearchAsync(new SearchQuery
        {
            From = DateTime.UtcNow.AddHours(-5),
            To = DateTime.UtcNow.AddMinutes(1)
        });
        Assert.Single(result.Items);
        Assert.Equal("recent", result.Items[0].Message);
    }

    [Fact]
    public async Task GetLevelCountsAsync_ReturnsCorrectCounts()
    {
        // Arrange
        await _store.WriteAsync([
            MakeEvent(level: LogLevel.Information),
            MakeEvent(level: LogLevel.Information),
            MakeEvent(level: LogLevel.Error)
        ]);

        // Act
        var counts = await _store.GetLevelCountsAsync(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

        // Assert
        Assert.Equal(2, counts["Information"]);
        Assert.Equal(1, counts["Error"]);
    }

    [Fact]
    public async Task GetServicesAsync_ReturnsDistinctSources()
    {
        // Arrange
        await _store.WriteAsync([
            MakeEvent(source: "svc-alpha"),
            MakeEvent(source: "svc-alpha"),
            MakeEvent(source: "svc-beta")
        ]);

        // Act
        var services = await _store.GetServicesAsync();

        // Assert
        Assert.Contains("svc-alpha", services);
        Assert.Contains("svc-beta", services);
        Assert.Equal(services.Distinct().Count(), services.Count);
    }

    [Fact]
    public async Task GetLogLevelsAsync_ReturnsDistinctLevelNames()
    {
        // Arrange
        await _store.WriteAsync([
            MakeEvent(level: LogLevel.Debug),
            MakeEvent(level: LogLevel.Debug),
            MakeEvent(level: LogLevel.Critical)
        ]);

        // Act
        var levels = await _store.GetLogLevelsAsync();

        // Assert
        Assert.Contains("Debug", levels);
        Assert.Contains("Critical", levels);
        Assert.Equal(levels.Distinct().Count(), levels.Count);
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsCorrectTotalAndBreakdown()
    {
        // Arrange
        await _store.WriteAsync([
            MakeEvent(level: LogLevel.Information, source: "svc-a"),
            MakeEvent(level: LogLevel.Error, source: "svc-a"),
            MakeEvent(level: LogLevel.Error, source: "svc-b")
        ]);

        // Act
        var stats = await _store.GetStatsAsync(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

        // Assert
        Assert.Equal(3, stats.TotalCount);
        Assert.Equal(1, stats.ByLevel["Information"]);
        Assert.Equal(2, stats.ByLevel["Error"]);
        Assert.Equal(2, stats.BySource["svc-a"]);
        Assert.Equal(1, stats.BySource["svc-b"]);
    }

    [Fact]
    public async Task GetStatsAsync_FilteredBySource_OnlyCountsThatSource()
    {
        // Arrange
        await _store.WriteAsync([
            MakeEvent(level: LogLevel.Information, source: "svc-x"),
            MakeEvent(level: LogLevel.Error, source: "svc-y")
        ]);

        // Act
        var stats = await _store.GetStatsAsync(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1), source: "svc-x");

        // Assert
        Assert.Equal(1, stats.TotalCount);
        Assert.True(stats.ByLevel.ContainsKey("Information"));
        Assert.False(stats.BySource.ContainsKey("svc-y"));
    }

    [Fact]
    public async Task GetLogsByTraceIdAsync_ReturnsAllMatchingCorrelationId()
    {
        // Arrange
        await _store.WriteAsync([
            MakeEvent(correlationId: "trace-abc", message: "span 1"),
            MakeEvent(correlationId: "trace-abc", message: "span 2"),
            MakeEvent(correlationId: "trace-xyz", message: "other trace")
        ]);

        // Act
        var logs = await _store.GetLogsByTraceIdAsync("trace-abc");

        // Assert
        Assert.Equal(2, logs.Count);
        Assert.All(logs, l => Assert.Equal("trace-abc", l.CorrelationId));
    }

    [Fact]
    public async Task GetLogsByTraceIdAsync_UnknownTraceId_ReturnsEmpty()
    {
        // Act
        var logs = await _store.GetLogsByTraceIdAsync("does-not-exist");

        // Assert
        Assert.Empty(logs);
    }

    [Fact]
    public async Task GetRelatedLogsAsync_ReturnsLogsWithinWindow()
    {
        // Arrange
        var anchor = DateTime.UtcNow;
        await _store.WriteAsync([
            MakeEvent(message: "before", timestamp: anchor.AddSeconds(-10)),
            MakeEvent(message: "anchor", timestamp: anchor),
            MakeEvent(message: "after", timestamp: anchor.AddSeconds(10)),
            MakeEvent(message: "far away", timestamp: anchor.AddMinutes(10))
        ]);
        var all = await _store.SearchAsync(new SearchQuery
        {
            From = anchor.AddSeconds(-1),
            To = anchor.AddSeconds(1),
            Text = "anchor"
        });
        var anchorId = all.Items[0].Id;

        // Act
        var related = await _store.GetRelatedLogsAsync(anchorId, windowSeconds: 15);

        // Assert
        Assert.Contains(related, l => l.Message == "before");
        Assert.Contains(related, l => l.Message == "after");
        Assert.DoesNotContain(related, l => l.Message == "anchor");
        Assert.DoesNotContain(related, l => l.Message == "far away");
    }

    [Fact]
    public async Task GetRelatedLogsAsync_NonExistentId_ReturnsEmpty()
    {
        // Act
        var related = await _store.GetRelatedLogsAsync(999_999, windowSeconds: 30);

        // Assert
        Assert.Empty(related);
    }

    [Fact]
    public async Task GetRecentErrorsAsync_ReturnsOnlyErrorAndAbove()
    {
        // Arrange
        await _store.WriteAsync([
            MakeEvent(level: LogLevel.Information, message: "info"),
            MakeEvent(level: LogLevel.Error, message: "error"),
            MakeEvent(level: LogLevel.Critical, message: "critical")
        ]);

        // Act
        var errors = await _store.GetRecentErrorsAsync(n: 10);

        // Assert
        Assert.All(errors, l => Assert.True(l.Level >= LogLevel.Error));
        Assert.Contains(errors, l => l.Message == "error");
        Assert.Contains(errors, l => l.Message == "critical");
        Assert.DoesNotContain(errors, l => l.Message == "info");
    }

    [Fact]
    public async Task GetRecentErrorsAsync_RespectsNLimit()
    {
        // Arrange
        await _store.WriteAsync(Enumerable.Range(1, 5).Select(i => MakeEvent(level: LogLevel.Error, message: $"err {i}")).ToArray());

        // Act
        var errors = await _store.GetRecentErrorsAsync(n: 3);

        // Assert
        Assert.Equal(3, errors.Count);
    }

    [Fact]
    public async Task TailLogsAsync_ReturnsLatestNLogs()
    {
        // Arrange
        var now = DateTime.UtcNow;
        await _store.WriteAsync(Enumerable.Range(1, 5)
            .Select(i => MakeEvent(message: $"tail {i}", timestamp: now.AddSeconds(i)))
            .ToArray());

        // Act
        var tail = await _store.TailLogsAsync(n: 3);

        // Assert
        Assert.Equal(3, tail.Count);
        // Most recent first
        Assert.True(tail[0].Timestamp >= tail[1].Timestamp);
    }

    [Fact]
    public async Task TailLogsAsync_FilteredBySource_OnlyReturnsThatSource()
    {
        // Arrange
        await _store.WriteAsync([
            MakeEvent(source: "svc-tail", message: "from tail svc"),
            MakeEvent(source: "svc-other", message: "from other")
        ]);

        // Act
        var tail = await _store.TailLogsAsync(n: 10, source: "svc-tail");

        // Assert
        Assert.All(tail, l => Assert.Equal("svc-tail", l.Source));
    }

    [Fact]
    public async Task GetLogRateAsync_ReturnsBucketsWithCounts()
    {
        // Arrange
        var now = DateTime.UtcNow;
        await _store.WriteAsync([
            MakeEvent(timestamp: now.AddMinutes(-4), message: "rate-1"),
            MakeEvent(timestamp: now.AddMinutes(-4).AddMilliseconds(1), message: "rate-2"),
            MakeEvent(timestamp: now.AddMinutes(-1), message: "rate-3")
        ]);

        // Act
        var buckets = await _store.GetLogRateAsync(now.AddMinutes(-10), now.AddMinutes(1), bucketMinutes: 1);

        // Assert
        Assert.NotEmpty(buckets);
        Assert.True(buckets.Sum(b => b.Count) >= 3);
    }

    [Fact]
    public async Task GetLogRateAsync_FilteredByLevel_OnlyCountsMatchingLevel()
    {
        // Arrange
        var now = DateTime.UtcNow;
        await _store.WriteAsync([
            MakeEvent(level: LogLevel.Error, timestamp: now),
            MakeEvent(level: LogLevel.Information, timestamp: now)
        ]);

        // Act
        var buckets = await _store.GetLogRateAsync(now.AddMinutes(-1), now.AddMinutes(1), bucketMinutes: 5, levelMin: (int)LogLevel.Error);

        // Assert
        Assert.Equal(1, buckets.Sum(b => b.Count));
    }

    [Fact]
    public async Task FindLogPatternsAsync_GroupsSimilarMessages()
    {
        // Arrange
        await _store.WriteAsync([
            MakeEvent(message: "user 123 logged in"),
            MakeEvent(message: "user 456 logged in"),
            MakeEvent(message: "user 789 logged in"),
            MakeEvent(message: "connection timeout after 30s")
        ]);

        // Act
        var patterns = await _store.FindLogPatternsAsync(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

        // Assert
        var loginPattern = patterns.FirstOrDefault(p => p.Pattern.Contains("logged in"));
        Assert.NotNull(loginPattern);
        Assert.Equal(3, loginPattern!.Count);
    }

    [Fact]
    public async Task FindLogPatternsAsync_RespectsTopN()
    {
        // Arrange
        await _store.WriteAsync(Enumerable.Range(1, 10)
            .Select(i => MakeEvent(message: $"unique message {i} here"))
            .ToArray());

        // Act
        var patterns = await _store.FindLogPatternsAsync(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1), topN: 3);

        // Assert
        Assert.True(patterns.Count <= 3);
    }

    [Fact]
    public async Task GetErrorSpikesAsync_ReturnsBucketsExceedingThreshold()
    {
        // Arrange
        var now = DateTime.UtcNow;
        await _store.WriteAsync(Enumerable.Range(1, 5)
            .Select(i => MakeEvent(level: LogLevel.Error, timestamp: now.AddMilliseconds(i), message: $"spike {i}"))
            .ToArray());

        // Act
        var spikes = await _store.GetErrorSpikesAsync(now.AddMinutes(-1), now.AddMinutes(1), threshold: 3, bucketMinutes: 5);

        // Assert
        Assert.NotEmpty(spikes);
        Assert.All(spikes, s => Assert.True(s.ErrorCount >= 3));
    }

    [Fact]
    public async Task GetErrorSpikesAsync_BelowThreshold_ReturnsEmpty()
    {
        // Arrange
        var now = DateTime.UtcNow;
        await _store.WriteAsync([MakeEvent(level: LogLevel.Error, timestamp: now)]);

        // Act
        var spikes = await _store.GetErrorSpikesAsync(now.AddMinutes(-1), now.AddMinutes(1), threshold: 100, bucketMinutes: 5);

        // Assert
        Assert.Empty(spikes);
    }

    [Fact]
    public async Task SearchAsync_FilterByProperty_ReturnsOnlyMatchingLogs()
    {
        // Arrange
        await _store.WriteAsync([
            new LogEvent
            {
                Timestamp = DateTime.UtcNow,
                Level = LogLevel.Information,
                Source = "test-svc",
                Message = "with userId",
                PropertiesJson = """{"userId":42,"action":"start"}"""
            },
            new LogEvent
            {
                Timestamp = DateTime.UtcNow,
                Level = LogLevel.Information,
                Source = "test-svc",
                Message = "different userId",
                PropertiesJson = """{"userId":99}"""
            },
            new LogEvent
            {
                Timestamp = DateTime.UtcNow,
                Level = LogLevel.Information,
                Source = "test-svc",
                Message = "no properties"
            }
        ]);

        // Act
        var result = await _store.SearchAsync(new SearchQuery
        {
            From = DateTime.UtcNow.AddMinutes(-1),
            To = DateTime.UtcNow.AddMinutes(1),
            Properties = new Dictionary<string, string> { ["userId"] = "42" }
        });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("with userId", result.Items[0].Message);
    }

    [Fact]
    public async Task SearchAsync_FilterByMultipleProperties_ReturnsOnlyFullMatch()
    {
        // Arrange
        await _store.WriteAsync([
            new LogEvent
            {
                Timestamp = DateTime.UtcNow,
                Level = LogLevel.Information,
                Source = "test-svc",
                Message = "both match",
                PropertiesJson = """{"userId":42,"action":"start"}"""
            },
            new LogEvent
            {
                Timestamp = DateTime.UtcNow,
                Level = LogLevel.Information,
                Source = "test-svc",
                Message = "only userId",
                PropertiesJson = """{"userId":42,"action":"stop"}"""
            }
        ]);

        // Act
        var result = await _store.SearchAsync(new SearchQuery
        {
            From = DateTime.UtcNow.AddMinutes(-1),
            To = DateTime.UtcNow.AddMinutes(1),
            Properties = new Dictionary<string, string> { ["userId"] = "42", ["action"] = "start" }
        });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("both match", result.Items[0].Message);
    }

    // ── MessageTemplate ──────────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_WithMessageTemplate_RoundTripsCorrectly()
    {
        // Arrange
        var evt = new LogEvent
        {
            Timestamp = DateTime.UtcNow,
            Level = LogLevel.Information,
            Source = "svc",
            Message = "Hello World",
            MessageTemplate = "Hello {Name}"
        };

        // Act
        await _store.WriteAsync([evt]);
        var result = await _store.SearchAsync(new SearchQuery
        {
            From = DateTime.UtcNow.AddMinutes(-1),
            To = DateTime.UtcNow.AddMinutes(1)
        });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("Hello {Name}", result.Items[0].MessageTemplate);
    }

    [Fact]
    public async Task SearchAsync_MessageTemplateFilter_ReturnsMatchingOnly()
    {
        // Arrange
        var match = new LogEvent { Timestamp = DateTime.UtcNow, Level = LogLevel.Information, Source = "svc", Message = "msg1", MessageTemplate = "Order {OrderId} placed" };
        var noMatch = new LogEvent { Timestamp = DateTime.UtcNow, Level = LogLevel.Information, Source = "svc", Message = "msg2", MessageTemplate = "User {UserId} logged in" };
        await _store.WriteAsync([match, noMatch]);

        // Act
        var result = await _store.SearchAsync(new SearchQuery
        {
            From = DateTime.UtcNow.AddMinutes(-1),
            To = DateTime.UtcNow.AddMinutes(1),
            MessageTemplate = "Order"
        });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("msg1", result.Items[0].Message);
    }

    [Fact]
    public async Task SearchAsync_MessageTemplateFilter_ExcludesNonMatching()
    {
        // Arrange
        var evt = new LogEvent { Timestamp = DateTime.UtcNow, Level = LogLevel.Information, Source = "svc", Message = "msg", MessageTemplate = "User {UserId} logged in" };
        await _store.WriteAsync([evt]);

        // Act
        var result = await _store.SearchAsync(new SearchQuery
        {
            From = DateTime.UtcNow.AddMinutes(-1),
            To = DateTime.UtcNow.AddMinutes(1),
            MessageTemplate = "Order"
        });

        // Assert
        Assert.Empty(result.Items);
    }

    // ── Log Quality ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLogQualityReportAsync_CountsWithoutTemplateAndProperties()
    {
        // Arrange
        var now = DateTime.UtcNow;
        await _store.WriteAsync([
            new LogEvent { Timestamp = now, Level = LogLevel.Information, Source = "api", Message = "User 1 logged in", MessageTemplate = "User {UserId} logged in", PropertiesJson = """{"userId":1}""" },
            new LogEvent { Timestamp = now, Level = LogLevel.Information, Source = "api", Message = "User 2 logged in" }, // no template, no props
            new LogEvent { Timestamp = now, Level = LogLevel.Error,       Source = "api", Message = "Something failed", MessageTemplate = "Something failed" } // no props
        ]);

        // Act
        var report = await _store.GetLogQualityReportAsync(now.AddMinutes(-1), now.AddMinutes(1));

        // Assert
        Assert.Equal(3, report.TotalCount);
        Assert.Equal(1, report.WithoutTemplate.Count);
        Assert.Equal(2, report.WithoutProperties.Count);
        Assert.NotEmpty(report.BySource);
        Assert.Single(report.SampleLogsWithoutTemplate);
        Assert.Equal("api", report.SampleLogsWithoutTemplate[0].Source);
    }

    [Fact]
    public async Task GetLogQualityReportAsync_AllHaveTemplate_WithoutTemplateIsZero()
    {
        // Arrange
        var now = DateTime.UtcNow;
        await _store.WriteAsync([
            new LogEvent { Timestamp = now, Level = LogLevel.Information, Source = "svc", Message = "done", MessageTemplate = "done" }
        ]);

        // Act
        var report = await _store.GetLogQualityReportAsync(now.AddMinutes(-1), now.AddMinutes(1));

        // Assert
        Assert.Equal(0, report.WithoutTemplate.Count);
        Assert.Equal(0.0, report.WithoutTemplate.Pct);
        Assert.Empty(report.SampleLogsWithoutTemplate);
    }

    [Fact]
    public async Task GetLogQualityReportAsync_FilteredBySource_OnlyCountsThatSource()
    {
        // Arrange
        var now = DateTime.UtcNow;
        await _store.WriteAsync([
            new LogEvent { Timestamp = now, Level = LogLevel.Information, Source = "svc-a", Message = "a" },
            new LogEvent { Timestamp = now, Level = LogLevel.Information, Source = "svc-b", Message = "b", MessageTemplate = "b" }
        ]);

        // Act
        var report = await _store.GetLogQualityReportAsync(now.AddMinutes(-1), now.AddMinutes(1), source: "svc-a");

        // Assert
        Assert.Equal(1, report.TotalCount);
        Assert.Equal(1, report.WithoutTemplate.Count);
    }

    [Fact]
    public async Task GetMessageTemplatesAsync_ReturnsDistinctTemplates()
    {
        // Arrange
        var now = DateTime.UtcNow;
        await _store.WriteAsync([
            new LogEvent { Timestamp = now, Level = LogLevel.Information, Source = "svc", Message = "m1", MessageTemplate = "User {Id} logged in" },
            new LogEvent { Timestamp = now, Level = LogLevel.Information, Source = "svc", Message = "m2", MessageTemplate = "User {Id} logged in" }, // duplicate
            new LogEvent { Timestamp = now, Level = LogLevel.Information, Source = "svc", Message = "m3", MessageTemplate = "Order {Id} placed" },
            new LogEvent { Timestamp = now, Level = LogLevel.Information, Source = "svc", Message = "m4" } // no template
        ]);

        // Act
        var templates = await _store.GetMessageTemplatesAsync();

        // Assert
        Assert.Equal(2, templates.Count);
        Assert.Contains("User {Id} logged in", templates);
        Assert.Contains("Order {Id} placed", templates);
    }

    [Fact]
    public async Task GetMessageTemplatesAsync_FilteredBySource_OnlyReturnsThatSource()
    {
        // Arrange
        var now = DateTime.UtcNow;
        await _store.WriteAsync([
            new LogEvent { Timestamp = now, Level = LogLevel.Information, Source = "svc-a", Message = "m1", MessageTemplate = "Template A" },
            new LogEvent { Timestamp = now, Level = LogLevel.Information, Source = "svc-b", Message = "m2", MessageTemplate = "Template B" }
        ]);

        // Act
        var templates = await _store.GetMessageTemplatesAsync(source: "svc-a");

        // Assert
        Assert.Single(templates);
        Assert.Equal("Template A", templates[0]);
    }

    [Fact]
    public async Task GetPropertyValuesAsync_ReturnsDistinctValues()
    {
        // Arrange
        var now = DateTime.UtcNow;
        await _store.WriteAsync([
            new LogEvent { Timestamp = now, Level = LogLevel.Information, Source = "svc", Message = "m1", PropertiesJson = """{"userId":"alice"}""" },
            new LogEvent { Timestamp = now, Level = LogLevel.Information, Source = "svc", Message = "m2", PropertiesJson = """{"userId":"bob"}""" },
            new LogEvent { Timestamp = now, Level = LogLevel.Information, Source = "svc", Message = "m3", PropertiesJson = """{"userId":"alice"}""" }, // duplicate
            new LogEvent { Timestamp = now, Level = LogLevel.Information, Source = "svc", Message = "m4", PropertiesJson = """{"action":"start"}""" } // different key
        ]);

        // Act
        var values = await _store.GetPropertyValuesAsync("userId", now.AddMinutes(-1), now.AddMinutes(1));

        // Assert
        Assert.Equal(2, values.Count);
        Assert.Contains("alice", values);
        Assert.Contains("bob", values);
    }

    [Fact]
    public async Task ClearAllAsync_RemovesAllEvents()
    {
        // Arrange
        await _store.WriteAsync([
            MakeEvent(message: "evt 1"),
            MakeEvent(message: "evt 2"),
            MakeEvent(message: "evt 3")
        ]);

        // Act
        await _store.ClearAllAsync();

        // Assert
        var result = await _store.SearchAsync(new SearchQuery
        {
            From = DateTime.UtcNow.AddMinutes(-10),
            To = DateTime.UtcNow.AddMinutes(1)
        });
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetPropertyValuesAsync_UnknownKey_ReturnsEmpty()
    {
        // Arrange
        var now = DateTime.UtcNow;
        await _store.WriteAsync([
            new LogEvent { Timestamp = now, Level = LogLevel.Information, Source = "svc", Message = "m1", PropertiesJson = """{"userId":"alice"}""" }
        ]);

        // Act
        var values = await _store.GetPropertyValuesAsync("nonExistent", now.AddMinutes(-1), now.AddMinutes(1));

        // Assert
        Assert.Empty(values);
    }
}
