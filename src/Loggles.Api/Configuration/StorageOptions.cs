namespace Loggles.Api.Configuration;

public sealed class StorageOptions
{
    public string Provider { get; set; } = "sqlite";
    public string ConnectionString { get; set; } = "Data Source=logs.db";

    public bool IsPostgres => Provider.Equals("postgres", StringComparison.OrdinalIgnoreCase);
}
