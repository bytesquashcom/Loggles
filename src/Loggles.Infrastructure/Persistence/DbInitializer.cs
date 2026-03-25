using Dapper;
using Microsoft.Data.Sqlite;

namespace Loggles.Infrastructure.Persistence;

public static class DbInitializer
{
    public static async Task InitAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync(ct);

        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS schema_version (
                version INTEGER PRIMARY KEY,
                applied_at TEXT NOT NULL
            );
        """);

        var version = await conn.ExecuteScalarAsync<int?>("SELECT MAX(version) FROM schema_version;");

        if (version is null or < 1)
        {
            await conn.ExecuteAsync("""
                CREATE TABLE IF NOT EXISTS logs (
                    id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp       TEXT    NOT NULL,
                    level           INTEGER NOT NULL,
                    source          TEXT    NOT NULL,
                    correlation_id  TEXT,
                    message         TEXT    NOT NULL,
                    exception       TEXT,
                    properties_json TEXT
                )
            """);
            await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_logs_timestamp      ON logs (timestamp)");
            await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_logs_level          ON logs (level)");
            await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_logs_source         ON logs (source)");
            await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_logs_correlation_id ON logs (correlation_id)");
            await conn.ExecuteAsync("INSERT INTO schema_version (version, applied_at) VALUES (1, datetime('now'))");
        }

        if (version is null or < 2)
        {
            await conn.ExecuteAsync("ALTER TABLE logs ADD COLUMN logger_name TEXT");
            await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_logs_logger_name ON logs (logger_name)");
            await conn.ExecuteAsync("INSERT INTO schema_version (version, applied_at) VALUES (2, datetime('now'))");
        }

        if (version is null or < 3)
        {
            await conn.ExecuteAsync("ALTER TABLE logs ADD COLUMN message_template TEXT");
            await conn.ExecuteAsync("INSERT INTO schema_version (version, applied_at) VALUES (3, datetime('now'))");
        }

        if (version is null or < 4)
        {
            await conn.ExecuteAsync("ALTER TABLE logs ADD COLUMN fingerprint TEXT");
            await conn.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS idx_logs_fingerprint ON logs (fingerprint) WHERE fingerprint IS NOT NULL");
            await conn.ExecuteAsync("INSERT INTO schema_version (version, applied_at) VALUES (4, datetime('now'))");
        }
    }
}
