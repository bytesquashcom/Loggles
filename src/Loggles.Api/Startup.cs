using Loggles.Api.Auth;
using Loggles.Api.Configuration;
using Loggles.Api.Mcp;
using Microsoft.AspNetCore.Authentication;
using Loggles.Core.Interfaces;
using Loggles.Infrastructure.BackgroundServices;
using Loggles.Infrastructure.Persistence;
using Loggles.Infrastructure.Queue;
using Loggles.Infrastructure.Tracking;
using Microsoft.AspNetCore.HttpLogging;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Options;

namespace Loggles.Api;

public sealed class Startup
{
    private readonly IConfiguration _configuration;

    public Startup(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        var mcpOptions = _configuration.GetSection("Mcp").Get<McpOptions>() ?? new McpOptions();
        var authOptions = _configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();

        services.AddControllers(options =>
        {
            if (!mcpOptions.OAuthEnabled)
            {
                options.Conventions.Add(new ExcludeOAuthControllersConvention());
            }
        });
        services.AddHttpLogging(o => o.LoggingFields = HttpLoggingFields.RequestPath |
                                                       HttpLoggingFields.RequestHeaders |
                                                       HttpLoggingFields.ResponseStatusCode |
                                                       HttpLoggingFields.ResponseBody);

        services.Configure<AuthOptions>(_configuration.GetSection("Auth"));

        if (authOptions.IsEnabled)
        {
            services.AddAuthentication("Bearer")
                .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthHandler>("Bearer", null);
            services.AddAuthorization();
        }
        else
        {
            // No API key configured — disable auth enforcement so [Authorize] becomes a no-op
            services.AddAuthentication("Bearer")
                .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthHandler>("Bearer", null);
            services.AddAuthorization(options =>
                options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                    .RequireAssertion(_ => true)
                    .Build());
        }

        services.Configure<CorsOptions>(_configuration.GetSection("Cors"));
        var corsOpts = _configuration.GetSection("Cors").Get<CorsOptions>() ?? new CorsOptions();

        services.AddCors(x =>
            x.AddDefaultPolicy(c =>
            {
                var policy = corsOpts.AllowedOrigins == "*"
                    ? c.AllowAnyOrigin()
                    : c.WithOrigins(corsOpts.GetOrigins());
                policy.AllowAnyHeader()
                      .AllowAnyMethod()
                      .WithExposedHeaders("Mcp-Session-Id", "Mcp-Protocol-Version");
            }));

        // Options
        services.Configure<StorageOptions>(_configuration.GetSection("Storage"));
        services.Configure<RetentionOptions>(_configuration.GetSection("Retention"));
        services.Configure<McpOptions>(_configuration.GetSection("Mcp"));
        // AuthOptions already configured above

        var storageOptions = _configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();

        // Core services
        services.AddSingleton<IMcpCallTracker, InMemoryMcpCallTracker>();
        services.AddSingleton<IIngestionQueue, InMemoryIngestionQueue>();
        services.AddSingleton<ILogStore>(_ => storageOptions.IsPostgres
            ? new PostgresLogStore(storageOptions.ConnectionString)
            : new SqliteLogStore(storageOptions.ConnectionString));

        // Background services
        services.AddHostedService<LogWriterService>();
        services.AddHostedService<RetentionService>();

        // MCP
        if (mcpOptions.Enabled)
        {
            var mcpBuilder = services.AddMcpServer()
                .WithToolsFromAssembly();

            if (string.Equals(mcpOptions.Transport, "http", StringComparison.OrdinalIgnoreCase))
            {
                mcpBuilder.WithHttpTransport();
            }
        }
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        // Debug: log all requests

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.UseRouting();
        var mcpOptions = _configuration.GetSection("Mcp").Get<McpOptions>() ?? new McpOptions();
        var authOptions = _configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseCors();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();

            if (mcpOptions.Enabled && string.Equals(mcpOptions.Transport, "http", StringComparison.OrdinalIgnoreCase))
            {
                var mcpEndpoint = endpoints.MapMcp("/mcp");
                if (authOptions.IsEnabled)
                    mcpEndpoint.RequireAuthorization();
            }
        });
    }
}