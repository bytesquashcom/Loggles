using Loggles.Api;
using Loggles.Api.Configuration;
using Loggles.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

namespace Loggles.Api;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureLogging((ctx, logging) =>
            {
                var cfg = ctx.Configuration.GetSection("SelfDiagnostics");
                if (cfg.GetValue<bool>("Enabled"))
                {
                    var endpoint = cfg.GetValue<string>("OtlpEndpoint") ?? "http://localhost:5000";
                    logging.AddOpenTelemetry(otel =>
                    {
                        otel.SetResourceBuilder(ResourceBuilder.CreateDefault()
                            .AddService("Loggles.Api"));
                        otel.AddOtlpExporter(otlp =>
                        {
                            otlp.Endpoint = new Uri($"{endpoint}/v1/logs");
                            otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
                            var apiKey = ctx.Configuration.GetSection("Auth").GetValue<string>("ApiKey");
                            if (!string.IsNullOrWhiteSpace(apiKey))
                                otlp.Headers = $"Authorization=Bearer {apiKey}";
                        });
                        otel.IncludeScopes = true;
                        otel.IncludeFormattedMessage = true;
                    });
                }
            })
            .ConfigureWebHostDefaults(web =>
            {
                web.UseStartup<Startup>();
            })
            .Build();

        var authOpts = host.Services.GetRequiredService<IOptions<AuthOptions>>().Value;
        if (!authOpts.IsEnabled)
        {
            var startupLogger = host.Services.GetRequiredService<ILogger<Program>>();
            startupLogger.LogWarning(
                "AUTH DISABLED: No API key configured. All endpoints are publicly accessible. " +
                "Set LOGGLES__AUTH__APIKEY to enable authentication.");
        }

        // Initialize DB schema before starting
        var storageOptions = host.Services.GetRequiredService<IOptions<StorageOptions>>().Value;
        if (storageOptions.IsPostgres)
            await PostgresDbInitializer.InitAsync(storageOptions.ConnectionString);
        else
            await DbInitializer.InitAsync(storageOptions.ConnectionString);

        await host.RunAsync();
    }
}
