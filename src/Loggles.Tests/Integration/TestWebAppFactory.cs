using Loggles.Core.Interfaces;
using Loggles.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Loggles.Tests.Integration;

public class TestWebAppFactory : WebApplicationFactory<Loggles.Api.Program>
{
    private readonly string _connStr;
    private readonly SqliteConnection _keepAlive;

    public TestWebAppFactory()
    {
        var dbName = Guid.NewGuid().ToString("N");
        _connStr = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connStr);
        _keepAlive.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILogStore>();
            services.AddSingleton<ILogStore>(_ =>
            {
                DbInitializer.InitAsync(_connStr).GetAwaiter().GetResult();
                return new SqliteLogStore(_connStr);
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _keepAlive.Dispose();
    }
}

/// <summary>Factory variant that injects a configured API key for auth tests.</summary>
public sealed class AuthenticatedTestWebAppFactory : TestWebAppFactory
{
    private readonly string _apiKey;

    public AuthenticatedTestWebAppFactory(string apiKey)
    {
        _apiKey = apiKey;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Auth:ApiKey", _apiKey);
        builder.UseSetting("Mcp:OAuthEnabled", "true");
    }
}

/// <summary>Factory variant that enables OAuth endpoints without an API key configured.</summary>
public sealed class OAuthEnabledTestWebAppFactory : TestWebAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Mcp:OAuthEnabled", "true");
    }
}

/// <summary>Factory variant that disables the OAuth discovery/token endpoints.</summary>
public sealed class OAuthDisabledTestWebAppFactory : TestWebAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Mcp:OAuthEnabled", "false");
    }
}
