using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

namespace Loggles.Demo.Otel;

/// <summary>
/// Sends a stream of varied log events to Loggles via OTLP/HTTP (POST /v1/logs).
/// Run alongside Loggles.Api to populate the log store for testing.
/// </summary>
internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        const string logglesEndpoint = "http://localhost:5000";

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddOpenTelemetry(otel =>
                {
                    otel.SetResourceBuilder(ResourceBuilder.CreateDefault()
                        .AddService("Loggles.Demo.Otel"));

                    otel.AddOtlpExporter(otlp =>
                    {
                        otlp.Endpoint = new Uri($"{logglesEndpoint}/v1/logs");
                        otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                    });

                    otel.IncludeScopes = true;
                    otel.IncludeFormattedMessage = true;
                });
            })
            .ConfigureServices(services =>
            {
                services.AddHostedService<DemoWorker>();
            })
            .Build();

        Console.WriteLine($"Loggles OTel Demo — exporting logs to {logglesEndpoint}/v1/logs");
        Console.WriteLine("Press Ctrl+C to stop.\n");

        await host.RunAsync();
    }
}

internal sealed class DemoWorker : BackgroundService
{
    private static readonly string[] Sources =
        ["OrderService", "PaymentService", "InventoryService", "AuthService", "RetentionService"];

    private static readonly Random Rng = new();

    private readonly ILoggerFactory _loggerFactory;

    public DemoWorker(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int count = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var source = Sources[Rng.Next(Sources.Length)];
            var logger = _loggerFactory.CreateLogger(source);
            var correlationId = Guid.NewGuid().ToString("N")[..8];
            var batchSize = EmitBatch(logger, source, correlationId);

            count += batchSize;
            Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] Emitted {batchSize} log records via OTel (total: {count}) — source: {source}");

            await Task.Delay(TimeSpan.FromMilliseconds(1200), stoppingToken).ContinueWith(_ => { });
        }
    }

    /// <summary>Emits a batch of log records and returns the count.</summary>
    private static int EmitBatch(ILogger logger, string source, string correlationId)
    {
        int count = 0;

        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["correlationId"] = correlationId,
            ["source"] = source
        }))
        {
            logger.LogInformation("Processing request {CorrelationId} — userId={UserId} action=start",
                correlationId, Rng.Next(1, 1000));
            count++;

            logger.LogDebug("Fetched record from database — table=orders rows={Rows}",
                Rng.Next(1, 50));
            count++;

            if (Rng.Next(3) == 0)
            {
                logger.LogWarning("Response time exceeded threshold — durationMs={DurationMs}",
                    Rng.Next(500, 3000));
                count++;
            }

            if (Rng.Next(5) == 0)
            {
                var ex = new InvalidOperationException("Sequence contains no elements");
                logger.LogError(ex, "Unhandled exception during request processing");
                count++;
            }

            logger.LogInformation("Request {CorrelationId} completed", correlationId);
            count++;
        }

        return count;
    }
}
