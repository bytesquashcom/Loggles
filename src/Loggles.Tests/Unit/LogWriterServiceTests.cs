using System.Runtime.CompilerServices;
using Loggles.Core.DTOs;
using Loggles.Core.Interfaces;
using Loggles.Core.Models;
using Loggles.Infrastructure.BackgroundServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Loggles.Tests.Unit;

public sealed class LogWriterServiceTests
{
    private static LogEvent MakeEvent() => new()
    {
        Timestamp = DateTime.UtcNow,
        Level     = Loggles.Core.Models.LogLevel.Information,
        Source    = "test",
        Message   = "test message"
    };

    /// <summary>
    /// When WriteAsync throws once and then succeeds, the batch should be written
    /// on the second attempt. Note: the retry delay is 2 seconds (2^1).
    /// </summary>
    [Fact]
    public async Task WriteBatchWithRetry_OnTransientFailure_RetriesAndSucceeds()
    {
        // Arrange
        var writeSucceeded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new FailNTimesThenSucceedStore(failCount: 1, onSuccess: writeSucceeded.SetResult);
        var queue = new SingleItemQueue(MakeEvent());
        var service = new LogWriterService(queue, store, NullLogger<LogWriterService>.Instance);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Wait for the store to signal success (up to 10s — includes ~2s retry delay)
        await writeSucceeded.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        // Assert — WriteAsync called at least twice (1 fail + 1 succeed)
        Assert.True(store.WriteCallCount >= 2);
        Assert.True(store.SucceededAtLeastOnce);
    }

    /// <summary>
    /// When WriteAsync always throws, an error should be logged after MaxRetries=3 attempts.
    /// Note: retry delays are 2s + 4s = ~6s total.
    /// </summary>
    [Fact]
    public async Task WriteBatchWithRetry_OnAllFailures_LogsError()
    {
        // Arrange
        var errorLogged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new CapturingLogger<LogWriterService>(onError: errorLogged.SetResult);
        var store = new AlwaysFailStore();
        var queue = new SingleItemQueue(MakeEvent());
        var service = new LogWriterService(queue, store, logger);

        // Act
        await service.StartAsync(CancellationToken.None);

        // Wait for the error log (up to 15s — includes 2s + 4s retry delays)
        await errorLogged.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await service.StopAsync(CancellationToken.None);

        // Assert
        Assert.True(logger.HasLoggedError);
        Assert.Equal(3, store.WriteCallCount);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    /// <summary>Yields exactly one item then completes — allows ExecuteAsync to drain and exit.</summary>
    private sealed class SingleItemQueue : IIngestionQueue
    {
        private readonly LogEvent _item;
        private int _yielded;

        public SingleItemQueue(LogEvent item) => _item = item;

        public ValueTask EnqueueAsync(LogEvent evt, CancellationToken ct = default) => ValueTask.CompletedTask;

        public async IAsyncEnumerable<LogEvent> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _yielded, 1) == 0)
                yield return _item;
        }
    }

    private sealed class FailNTimesThenSucceedStore : ILogStore
    {
        private readonly int _failCount;
        private readonly Action _onSuccess;
        private int _calls;

        public int WriteCallCount => _calls;
        public bool SucceededAtLeastOnce { get; private set; }

        public FailNTimesThenSucceedStore(int failCount, Action onSuccess)
        {
            _failCount = failCount;
            _onSuccess = onSuccess;
        }

        public Task WriteAsync(IReadOnlyList<LogEvent> events, CancellationToken ct = default)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call <= _failCount)
                throw new InvalidOperationException("Transient failure");
            SucceededAtLeastOnce = true;
            _onSuccess();
            return Task.CompletedTask;
        }

        public Task<SearchResult> SearchAsync(SearchQuery q, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<LogEvent?> GetByIdAsync(long id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Dictionary<string, int>> GetLevelCountsAsync(DateTime from, DateTime to, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<string>> GetServicesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<string>> GetPropertiesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<string>> GetLogLevelsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<LogStats> GetStatsAsync(DateTime from, DateTime to, string? source = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<LogEvent>> GetLogsByTraceIdAsync(string traceId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<LogEvent>> GetRelatedLogsAsync(long id, int windowSeconds = 30, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<LogEvent>> GetRecentErrorsAsync(int n = 50, string? source = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<LogEvent>> TailLogsAsync(int n = 50, string? source = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<LogRateBucket>> GetLogRateAsync(DateTime from, DateTime to, int bucketMinutes = 1, string? source = null, int? levelMin = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<LogPattern>> FindLogPatternsAsync(DateTime from, DateTime to, string? source = null, int? levelMin = null, int topN = 20, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<ErrorSpike>> GetErrorSpikesAsync(DateTime from, DateTime to, int threshold = 10, int bucketMinutes = 5, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<LogQualityReport> GetLogQualityReportAsync(DateTime from, DateTime to, string? source = null, int sampleSize = 5, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<string>> GetMessageTemplatesAsync(string? source = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<string>> GetPropertyValuesAsync(string key, DateTime from, DateTime to, string? source = null, int limit = 50, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ClearAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class AlwaysFailStore : ILogStore
    {
        private int _calls;
        public int WriteCallCount => _calls;

        public Task WriteAsync(IReadOnlyList<LogEvent> events, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            throw new InvalidOperationException("Permanent failure");
        }

        public Task<SearchResult> SearchAsync(SearchQuery q, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<LogEvent?> GetByIdAsync(long id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Dictionary<string, int>> GetLevelCountsAsync(DateTime from, DateTime to, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<string>> GetServicesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<string>> GetPropertiesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<string>> GetLogLevelsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<LogStats> GetStatsAsync(DateTime from, DateTime to, string? source = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<LogEvent>> GetLogsByTraceIdAsync(string traceId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<LogEvent>> GetRelatedLogsAsync(long id, int windowSeconds = 30, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<LogEvent>> GetRecentErrorsAsync(int n = 50, string? source = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<LogEvent>> TailLogsAsync(int n = 50, string? source = null, DateTime? from = null, DateTime? to = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<LogRateBucket>> GetLogRateAsync(DateTime from, DateTime to, int bucketMinutes = 1, string? source = null, int? levelMin = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<LogPattern>> FindLogPatternsAsync(DateTime from, DateTime to, string? source = null, int? levelMin = null, int topN = 20, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<ErrorSpike>> GetErrorSpikesAsync(DateTime from, DateTime to, int threshold = 10, int bucketMinutes = 5, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<LogQualityReport> GetLogQualityReportAsync(DateTime from, DateTime to, string? source = null, int sampleSize = 5, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<string>> GetMessageTemplatesAsync(string? source = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<string>> GetPropertyValuesAsync(string key, DateTime from, DateTime to, string? source = null, int limit = 50, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ClearAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly Action _onError;
        public bool HasLoggedError { get; private set; }

        public CapturingLogger(Action onError) => _onError = onError;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= Microsoft.Extensions.Logging.LogLevel.Error && !HasLoggedError)
            {
                HasLoggedError = true;
                _onError();
            }
        }

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}
