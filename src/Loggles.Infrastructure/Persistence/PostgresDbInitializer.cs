using Dapper;
using Npgsql;

namespace Loggles.Infrastructure.Persistence;

public static class PostgresDbInitializer
{
    public static async Task InitAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS schema_version (
                version    INTEGER PRIMARY KEY,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );
        """);

        var version = await conn.ExecuteScalarAsync<int?>("SELECT MAX(version) FROM schema_version;");

        if (version is null or < 1)
        {
            await conn.ExecuteAsync("""
                CREATE TABLE IF NOT EXISTS logs (
                    id               BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    timestamp        TIMESTAMPTZ NOT NULL,
                    level            SMALLINT    NOT NULL,
                    source           TEXT        NOT NULL,
                    logger_name      TEXT,
                    correlation_id   TEXT,
                    message          TEXT        NOT NULL,
                    message_template TEXT,
                    exception        TEXT,
                    properties_json  JSONB,
                    fingerprint      TEXT UNIQUE
                );
            """);
            await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_logs_timestamp      ON logs (timestamp DESC)");
            await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_logs_level          ON logs (level)");
            await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_logs_source         ON logs (source)");
            await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_logs_correlation_id ON logs (correlation_id)");
            await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_logs_logger_name    ON logs (logger_name)");
            await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_logs_properties_gin ON logs USING GIN (properties_json)");
            await conn.ExecuteAsync("INSERT INTO schema_version (version) VALUES (1)");
        }
    }
}
