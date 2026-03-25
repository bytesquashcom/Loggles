using Loggles.Core.Interfaces;
using Loggles.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Loggles.Infrastructure.BackgroundServices;

public sealed class LogWriterService : BackgroundService
{
    private const int BatchSize = 500;
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(1);
    private const int MaxRetries = 3;

    private readonly IIngestionQueue _queue;
    private readonly ILogStore _store;
    private readonly ILogger<LogWriterService> _logger;

    public LogWriterService(IIngestionQueue queue, ILogStore store, ILogger<LogWriterService> logger)
    {
        _queue = queue;
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<LogEvent>(BatchSize);

        await foreach (var evt in _queue.ReadAllAsync(stoppingToken))
        {
            batch.Add(evt);

            // Drain any immediately available items up to batch size
            while (batch.Count < BatchSize)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(FlushTimeout);

                try
                {
                    await foreach (var next in _queue.ReadAllAsync(cts.Token))
                    {
                        batch.Add(next);
                        if (batch.Count >= BatchSize) break;
                    }
                    break; // flush timeout hit cleanly
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    break; // flush timeout — write what we have
                }
            }

            await WriteBatchWithRetryAsync(batch, stoppingToken);
            batch.Clear();
        }

        // Drain remaining on shutdown
        if (batch.Count > 0)
            await WriteBatchWithRetryAsync(batch, CancellationToken.None);
    }

    private async Task WriteBatchWithRetryAsync(List<LogEvent> batch, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await _store.WriteAsync(batch, ct);
                return;
            }
            catch (Exception ex) when (attempt < MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning(ex, "DB write failed (attempt {Attempt}), retrying in {Delay}s", attempt, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DB write failed after {MaxRetries} attempts; dropping {Count} events", MaxRetries, batch.Count);
            }
        }
    }
}
