using System.Text.Json;
using Messaging.Api.Interfaces;
using Messaging.Api.Models;
using Microsoft.Data.Sqlite;

namespace Messaging.Api.Services;

public class OutboxRepository : IOutboxRepository
{
    // SQLITE_CONSTRAINT_UNIQUE
    private const int UniqueConstraintError = 2067;

    private readonly string _connectionString;

    public OutboxRepository(IConfiguration configuration)
    {
        var dbPath = configuration["Outbox:DbPath"] ?? "data/outbox.db";
        _connectionString = $"Data Source={dbPath}";
    }

    private SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static string ToIso(DateTime value) => value.ToUniversalTime().ToString("o");

    public async Task<bool> TryInsertAsync(OutboxMessage msg)
    {
        using var connection = CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO message_outbox
                (id, correlation_id, system, subject_code, notification_type, recipients,
                 metadata, status, attempts, last_error, locked_until, is_client, url,
                 idempotency_system, idempotency_key, created_at, updated_at)
            VALUES
                (@id, @correlationId, @system, @subjectCode, @notificationType, @recipients,
                 @metadata, @status, @attempts, @lastError, @lockedUntil, @isClient, @url,
                 @idempotencySystem, @idempotencyKey, @createdAt, @updatedAt)
            """;
        command.Parameters.AddWithValue("@id", msg.Id);
        command.Parameters.AddWithValue("@correlationId", msg.CorrelationId);
        command.Parameters.AddWithValue("@system", msg.System);
        command.Parameters.AddWithValue("@subjectCode", msg.SubjectCode);
        command.Parameters.AddWithValue("@notificationType", msg.NotificationType);
        command.Parameters.AddWithValue("@recipients", JsonSerializer.Serialize(msg.Recipients));
        command.Parameters.AddWithValue("@metadata", (object?)msg.Metadata ?? DBNull.Value);
        command.Parameters.AddWithValue("@status", msg.Status);
        command.Parameters.AddWithValue("@attempts", msg.Attempts);
        command.Parameters.AddWithValue("@lastError", (object?)msg.LastError ?? DBNull.Value);
        command.Parameters.AddWithValue("@lockedUntil", msg.LockedUntil.HasValue ? ToIso(msg.LockedUntil.Value) : DBNull.Value);
        command.Parameters.AddWithValue("@isClient", msg.IsClient.HasValue ? (msg.IsClient.Value ? 1 : 0) : DBNull.Value);
        command.Parameters.AddWithValue("@url", (object?)msg.Url ?? DBNull.Value);
        command.Parameters.AddWithValue("@idempotencySystem", (object?)msg.IdempotencySystem ?? DBNull.Value);
        command.Parameters.AddWithValue("@idempotencyKey", (object?)msg.IdempotencyKey ?? DBNull.Value);
        command.Parameters.AddWithValue("@createdAt", ToIso(msg.CreatedAt));
        command.Parameters.AddWithValue("@updatedAt", ToIso(msg.UpdatedAt));

        try
        {
            await command.ExecuteNonQueryAsync();
            return true;
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == UniqueConstraintError
                                          && ex.Message.Contains("idempotency_key", StringComparison.Ordinal))
        {
            return false;
        }
    }

    public async Task<OutboxMessage?> GetByIdempotencyKeyAsync(string system, string idempotencyKey)
    {
        using var connection = CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM message_outbox
            WHERE idempotency_system = @system AND idempotency_key = @idempotencyKey
            LIMIT 1
            """;
        command.Parameters.AddWithValue("@system", system);
        command.Parameters.AddWithValue("@idempotencyKey", idempotencyKey);
        using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task AddDeliveredRecipientsAsync(string correlationId, IEnumerable<string> recipients)
    {
        // União no próprio UPDATE (atômico): reports simultâneos não se sobrescrevem
        using var connection = CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE message_outbox
            SET delivered_recipients = (
                SELECT json_group_array(value) FROM (
                    SELECT MIN(value) AS value FROM (
                        SELECT value FROM json_each(COALESCE(message_outbox.delivered_recipients, '[]'))
                        UNION ALL
                        SELECT value FROM json_each(@recipients)
                    )
                    GROUP BY lower(value)
                )
            )
            WHERE correlation_id = @correlationId
            """;
        command.Parameters.AddWithValue("@recipients", JsonSerializer.Serialize(recipients));
        command.Parameters.AddWithValue("@correlationId", correlationId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<OutboxMessage?> GetByCorrelationIdAsync(string correlationId)
    {
        using var connection = CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM message_outbox WHERE correlation_id = @correlationId LIMIT 1";
        command.Parameters.AddWithValue("@correlationId", correlationId);
        using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<bool> UpdateStatusAsync(string correlationId, string status, string? lastError = null)
    {
        using var connection = CreateConnection();
        using var command = connection.CreateCommand();

        // SENT sempre vence (entrega tardia); SENT e DEAD são terminais para os demais status
        command.CommandText = status == OutboxStatus.Sent
            ? """
              UPDATE message_outbox
              SET status = @status, last_error = @lastError, updated_at = @updatedAt
              WHERE correlation_id = @correlationId
              """
            : """
              UPDATE message_outbox
              SET status = @status, last_error = @lastError, updated_at = @updatedAt
              WHERE correlation_id = @correlationId AND status NOT IN ('SENT', 'DEAD')
              """;
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@lastError", (object?)lastError ?? DBNull.Value);
        command.Parameters.AddWithValue("@updatedAt", ToIso(DateTime.UtcNow));
        command.Parameters.AddWithValue("@correlationId", correlationId);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task IncrementAttemptsAsync(string correlationId)
    {
        using var connection = CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE message_outbox
            SET attempts = attempts + 1, updated_at = @updatedAt
            WHERE correlation_id = @correlationId
            """;
        command.Parameters.AddWithValue("@updatedAt", ToIso(DateTime.UtcNow));
        command.Parameters.AddWithValue("@correlationId", correlationId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<OutboxMessage>> ClaimRetryCandidatesAsync(int maxAttempts, int pendingTimeoutSeconds, int unknownTimeoutSeconds, int lockSeconds, int limit)
    {
        var now = DateTime.UtcNow;
        var pendingCutoff = now.AddSeconds(-pendingTimeoutSeconds);
        var unknownCutoff = now.AddSeconds(-unknownTimeoutSeconds);

        using var connection = CreateConnection();
        using var command = connection.CreateCommand();

        // Seleção + lock em um único UPDATE atômico: execuções sobrepostas
        // nunca revendicam a mesma mensagem (só retorna linhas efetivamente lockadas).
        // PENDING nunca publicada (attempts = 0): cutoff curto, para recuperação rápida.
        // PENDING já publicada (attempts >= 1): cutoff longo, para que backlog do worker
        // não vire UNKNOWN e UNKNOWN signifique de fato "report perdido".
        command.CommandText = """
            UPDATE message_outbox
            SET locked_until = @lockUntil
            WHERE correlation_id IN (
                SELECT correlation_id FROM message_outbox
                WHERE attempts <= @maxAttempts
                  AND (
                        status = 'FAILED'
                     OR (status = 'PENDING' AND attempts = 0 AND updated_at < @pendingCutoff)
                     OR (status = 'PENDING' AND attempts >= 1 AND updated_at < @unknownCutoff)
                  )
                  AND (locked_until IS NULL OR locked_until < @now)
                ORDER BY created_at
                LIMIT @limit
            )
            RETURNING *
            """;
        command.Parameters.AddWithValue("@lockUntil", ToIso(now.AddSeconds(lockSeconds)));
        command.Parameters.AddWithValue("@maxAttempts", maxAttempts);
        command.Parameters.AddWithValue("@pendingCutoff", ToIso(pendingCutoff));
        command.Parameters.AddWithValue("@unknownCutoff", ToIso(unknownCutoff));
        command.Parameters.AddWithValue("@now", ToIso(now));
        command.Parameters.AddWithValue("@limit", limit);

        var result = new List<OutboxMessage>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(Map(reader));
        return result;
    }

    public async Task<int> CountByStatusAsync(string status)
    {
        using var connection = CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM message_outbox WHERE status = @status";
        command.Parameters.AddWithValue("@status", status);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task ReleaseLockAsync(string correlationId)
    {
        using var connection = CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE message_outbox SET locked_until = NULL WHERE correlation_id = @correlationId";
        command.Parameters.AddWithValue("@correlationId", correlationId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<(List<OutboxMessage> Items, int Total)> ListAsync(
        string? status, string? system, string? notificationType,
        DateTime? from, DateTime? to, int page, int pageSize)
    {
        var filters = new List<string>();
        var parameters = new Dictionary<string, object>();

        if (!string.IsNullOrWhiteSpace(status))
        {
            filters.Add("status = @status");
            parameters["@status"] = status.ToUpperInvariant();
        }
        if (!string.IsNullOrWhiteSpace(system))
        {
            filters.Add("system = @system COLLATE NOCASE");
            parameters["@system"] = system;
        }
        if (!string.IsNullOrWhiteSpace(notificationType))
        {
            filters.Add("notification_type = @notificationType COLLATE NOCASE");
            parameters["@notificationType"] = notificationType;
        }
        if (from.HasValue)
        {
            filters.Add("created_at >= @from");
            parameters["@from"] = ToIso(from.Value);
        }
        if (to.HasValue)
        {
            filters.Add("created_at <= @to");
            parameters["@to"] = ToIso(to.Value);
        }

        var where = filters.Count > 0 ? $"WHERE {string.Join(" AND ", filters)}" : "";

        using var connection = CreateConnection();

        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(*) FROM message_outbox {where}";
        foreach (var (key, value) in parameters)
            countCommand.Parameters.AddWithValue(key, value);
        var total = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT * FROM message_outbox {where}
            ORDER BY created_at DESC
            LIMIT @pageSize OFFSET @offset
            """;
        foreach (var (key, value) in parameters)
            command.Parameters.AddWithValue(key, value);
        command.Parameters.AddWithValue("@pageSize", pageSize);
        command.Parameters.AddWithValue("@offset", (page - 1) * pageSize);

        var items = new List<OutboxMessage>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            items.Add(Map(reader));

        return (items, total);
    }

    private static OutboxMessage Map(SqliteDataReader reader)
    {
        string? GetNullableString(string column) =>
            reader.IsDBNull(reader.GetOrdinal(column)) ? null : reader.GetString(reader.GetOrdinal(column));

        var recipientsJson = reader.GetString(reader.GetOrdinal("recipients"));
        var lockedUntil = GetNullableString("locked_until");
        var deliveredJson = GetNullableString("delivered_recipients");
        var isClientOrdinal = reader.GetOrdinal("is_client");

        return new OutboxMessage
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            CorrelationId = reader.GetString(reader.GetOrdinal("correlation_id")),
            System = reader.GetString(reader.GetOrdinal("system")),
            SubjectCode = reader.GetString(reader.GetOrdinal("subject_code")),
            NotificationType = reader.GetString(reader.GetOrdinal("notification_type")),
            Recipients = JsonSerializer.Deserialize<List<string>>(recipientsJson) ?? new List<string>(),
            Metadata = GetNullableString("metadata"),
            Status = reader.GetString(reader.GetOrdinal("status")),
            Attempts = reader.GetInt32(reader.GetOrdinal("attempts")),
            LastError = GetNullableString("last_error"),
            LockedUntil = lockedUntil is null ? null : DateTime.Parse(lockedUntil).ToUniversalTime(),
            IsClient = reader.IsDBNull(isClientOrdinal) ? null : reader.GetInt32(isClientOrdinal) == 1,
            Url = GetNullableString("url"),
            DeliveredRecipients = deliveredJson is null
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(deliveredJson) ?? new List<string>(),
            IdempotencySystem = GetNullableString("idempotency_system"),
            IdempotencyKey = GetNullableString("idempotency_key"),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at"))).ToUniversalTime(),
            UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("updated_at"))).ToUniversalTime()
        };
    }
}
