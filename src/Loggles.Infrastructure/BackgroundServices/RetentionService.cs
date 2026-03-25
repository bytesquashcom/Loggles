using Loggles.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Loggles.Infrastructure.BackgroundServices;

public sealed class RetentionOptions
{
    public int Hours { get; set; } = 48;
    public int PurgeIntervalMinutes { get; set; } = 15;
}

public sealed class RetentionService : BackgroundService
{
    private readonly ILogStore _store;
    private readonly RetentionOptions _options;
    private readonly ILogger<RetentionService> _logger;

    public RetentionService(ILogStore store, IOptions<RetentionOptions> options, ILogger<RetentionService> logger)
    {
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddHours(-_options.Hours);
                await _store.DeleteOlderThanAsync(cutoff, stoppingToken);
                _logger.LogDebug("Retention purge complete. Cutoff: {Cutoff}", cutoff);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Retention purge failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(_options.PurgeIntervalMinutes), stoppingToken);
        }
    }
}
