using Microsoft.Data.Sqlite;

namespace Messaging.Api.Data;

public static class OutboxDbInitializer
{
    public static void Initialize(string dbPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA busy_timeout=5000;

            CREATE TABLE IF NOT EXISTS message_outbox (
                id                TEXT PRIMARY KEY,
                correlation_id    TEXT NOT NULL,
                system            TEXT NOT NULL,
                subject_code      TEXT NOT NULL,
                notification_type TEXT NOT NULL,
                recipients        TEXT NOT NULL,
                metadata          TEXT NULL,
                status            TEXT NOT NULL,
                attempts          INTEGER NOT NULL DEFAULT 0,
                last_error        TEXT NULL,
                locked_until      TEXT NULL,
                is_client         INTEGER NULL,
                url               TEXT NULL,
                created_at        TEXT NOT NULL,
                updated_at        TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_outbox_status ON message_outbox(status);
            CREATE INDEX IF NOT EXISTS idx_outbox_correlation ON message_outbox(correlation_id);
            """;
        command.ExecuteNonQuery();

        AddColumnIfMissing(connection, "delivered_recipients", "TEXT NULL");
        AddColumnIfMissing(connection, "idempotency_system", "TEXT NULL");
        AddColumnIfMissing(connection, "idempotency_key", "TEXT NULL");

        // Chave única por (sistema, chave): sem concatenação, sem colisão
        using var index = connection.CreateCommand();
        index.CommandText = """
            DROP INDEX IF EXISTS idx_outbox_idempotency_key;
            CREATE UNIQUE INDEX IF NOT EXISTS idx_outbox_idempotency
                ON message_outbox(idempotency_system, idempotency_key) WHERE idempotency_key IS NOT NULL;
            """;
        index.ExecuteNonQuery();
    }

    private static void AddColumnIfMissing(SqliteConnection connection, string column, string definition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('message_outbox') WHERE name = @column";
        check.Parameters.AddWithValue("@column", column);
        if (Convert.ToInt32(check.ExecuteScalar()) > 0)
            return;

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE message_outbox ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }
}
