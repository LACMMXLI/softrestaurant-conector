using System.Text.Json;
using Microsoft.Data.Sqlite;
using RestaurantAgent.Sync.Contracts;

namespace RestaurantAgent.Extractor;

internal sealed record PendingBatch(string Id, SyncBatch Batch, int Attempts);

internal sealed class SyncOutbox(string databasePath)
{
    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

    public async Task InitializeAsync(CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(ct);
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS pending_batches (
                id TEXT PRIMARY KEY,
                payload TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                attempts INTEGER NOT NULL DEFAULT 0,
                last_error TEXT NULL,
                next_attempt_at_utc TEXT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task EnqueueAsync(SyncBatch batch, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(batch, ExtractionJob.JsonOptions);
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(ct);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO pending_batches (id, payload, created_at_utc)
            VALUES ($id, $payload, $created)
            ON CONFLICT(id) DO UPDATE SET payload = excluded.payload;
            """;
        command.Parameters.AddWithValue("$id", batch.BatchId);
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$created", batch.CreatedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<PendingBatch?> GetNextAsync(CancellationToken ct)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(ct);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, payload, attempts
            FROM pending_batches
            WHERE next_attempt_at_utc IS NULL OR next_attempt_at_utc <= $now
            ORDER BY created_at_utc
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var batch = JsonSerializer.Deserialize<SyncBatch>(reader.GetString(1), ExtractionJob.JsonOptions)
            ?? throw new InvalidDataException("La cola contiene un lote JSON inválido.");
        return new PendingBatch(reader.GetString(0), batch, reader.GetInt32(2));
    }

    public async Task MarkSentAsync(string id, CancellationToken ct)
    {
        await ExecuteAsync("DELETE FROM pending_batches WHERE id = $id;", id, null, ct);
    }

    public async Task MarkFailedAsync(PendingBatch pending, string error, CancellationToken ct)
    {
        var delaySeconds = Math.Min(300, Math.Pow(2, Math.Min(8, pending.Attempts + 1)) * 5);
        var next = DateTime.UtcNow.AddSeconds(delaySeconds).ToString("O");
        await ExecuteAsync("""
            UPDATE pending_batches
            SET attempts = attempts + 1,
                last_error = $value,
                next_attempt_at_utc = $next
            WHERE id = $id;
            """, pending.Id, error[..Math.Min(error.Length, 1000)], ct, next);
    }

    public async Task<int> CountAsync(CancellationToken ct)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(ct);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pending_batches;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    private async Task ExecuteAsync(string sql, string id, string? value, CancellationToken ct, string? next = null)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(ct);
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id);
        if (value is not null) command.Parameters.AddWithValue("$value", value);
        if (next is not null) command.Parameters.AddWithValue("$next", next);
        await command.ExecuteNonQueryAsync(ct);
    }
}
