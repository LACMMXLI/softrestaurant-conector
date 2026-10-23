using System.Text.Json;
using System.Threading.Channels;
using Npgsql;

namespace RestaurantAgent.CentralApi;

internal sealed record ActivityEntry(
    DateTime OccurredAtUtc,
    Guid? UserId,
    string? UserEmail,
    string? UserName,
    string Source,
    string EventType,
    string Action,
    string? View,
    string? Method,
    string? Path,
    int? Status,
    int? DurationMs,
    string? BranchCode,
    Guid? BusinessId,
    string? Ip,
    string? UserAgent,
    string? SessionRef,
    string MetadataJson);

/// <summary>
/// Escribe el registro de actividad fuera de la petición del usuario: las peticiones solo
/// encolan (sin esperar a la base de datos) y este servicio inserta por lotes. Si la cola se
/// llena se descartan los eventos más antiguos; perder una línea de bitácora nunca debe frenar
/// ni romper el dashboard. También depura el historial vencido según
/// <c>ACTIVITY_LOG_RETENTION_DAYS</c>.
/// </summary>
internal sealed class ActivityLogger(NpgsqlDataSource dataSource, ApiOptions options, ILogger<ActivityLogger> logger) : BackgroundService
{
    private const int MaxBatchSize = 200;
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(6);

    private readonly Channel<ActivityEntry> channel = Channel.CreateBounded<ActivityEntry>(
        new BoundedChannelOptions(5000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public void Record(ActivityEntry entry) => channel.Writer.TryWrite(entry);

    public static string SerializeMetadata(IReadOnlyDictionary<string, object?> metadata) =>
        JsonSerializer.Serialize(metadata);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextPurge = DateTime.UtcNow;
        try
        {
            while (await channel.Reader.WaitToReadAsync(stoppingToken))
            {
                await WriteBatchAsync(Drain(), stoppingToken);
                if (DateTime.UtcNow >= nextPurge)
                {
                    await PurgeAsync(stoppingToken);
                    nextPurge = DateTime.UtcNow + PurgeInterval;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Apagado ordenado: se intenta guardar lo que quedó en cola.
        }

        var remaining = Drain();
        if (remaining.Count > 0) await WriteBatchAsync(remaining, CancellationToken.None);
    }

    private List<ActivityEntry> Drain()
    {
        var batch = new List<ActivityEntry>();
        while (batch.Count < MaxBatchSize && channel.Reader.TryRead(out var entry)) batch.Add(entry);
        return batch;
    }

    private async Task WriteBatchAsync(List<ActivityEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0) return;
        try
        {
            await using var batch = dataSource.CreateBatch();
            foreach (var entry in entries) batch.BatchCommands.Add(BuildInsert(entry));
            await batch.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo guardar un lote de {Count} eventos de actividad.", entries.Count);
        }
    }

    private async Task PurgeAsync(CancellationToken ct)
    {
        try
        {
            await using var command = dataSource.CreateCommand(
                "DELETE FROM activity_log WHERE occurred_at < now() - make_interval(days => $1);");
            command.Parameters.AddWithValue(options.ActivityRetentionDays);
            var removed = await command.ExecuteNonQueryAsync(ct);
            if (removed > 0) logger.LogInformation("Depuración de actividad: {Removed} eventos con más de {Days} días.", removed, options.ActivityRetentionDays);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo depurar el historial de actividad.");
        }
    }

    /// <summary>
    /// El nombre de sucursal/negocio se copia al momento del evento (snapshot) con un único
    /// INSERT ... SELECT, así el historial no depende de que el nombre actual siga siendo el mismo.
    /// </summary>
    internal static NpgsqlBatchCommand BuildInsert(ActivityEntry e)
    {
        var command = new NpgsqlBatchCommand("""
            INSERT INTO activity_log (
                occurred_at, user_id, user_email, user_name, source, event_type, action, view,
                method, path, status, duration_ms, business_id, business_name,
                branch_id, branch_code, branch_name, ip, user_agent, session_ref, metadata)
            SELECT $1::timestamptz, $2::uuid, $3::text, $4::text, $5::text, $6::text, $7::text, $8::text,
                   $9::text, $10::text, $11::integer, $12::integer, bz.id, bz.name,
                   b.id, COALESCE(b.code, $13::text), b.name, $15::text, $16::text, $17::text, $18::text::jsonb
            FROM (SELECT 1) AS one
            LEFT JOIN branches b ON b.code = $13::text
            LEFT JOIN businesses bz ON bz.id = COALESCE(b.business_id, $14::uuid);
            """);
        command.Parameters.AddWithValue(e.OccurredAtUtc);
        command.Parameters.AddWithValue(NullIfNone(e.UserId));
        command.Parameters.AddWithValue(NullIfNone(e.UserEmail));
        command.Parameters.AddWithValue(NullIfNone(e.UserName));
        command.Parameters.AddWithValue(e.Source);
        command.Parameters.AddWithValue(e.EventType);
        command.Parameters.AddWithValue(e.Action);
        command.Parameters.AddWithValue(NullIfNone(e.View));
        command.Parameters.AddWithValue(NullIfNone(e.Method));
        command.Parameters.AddWithValue(NullIfNone(e.Path));
        command.Parameters.AddWithValue(NullIfNone(e.Status));
        command.Parameters.AddWithValue(NullIfNone(e.DurationMs));
        command.Parameters.AddWithValue(NullIfNone(e.BranchCode));
        command.Parameters.AddWithValue(NullIfNone(e.BusinessId));
        command.Parameters.AddWithValue(NullIfNone(e.Ip));
        command.Parameters.AddWithValue(NullIfNone(e.UserAgent));
        command.Parameters.AddWithValue(NullIfNone(e.SessionRef));
        command.Parameters.AddWithValue(e.MetadataJson);
        return command;
    }

    private static object NullIfNone(object? value) => value ?? DBNull.Value;
}
