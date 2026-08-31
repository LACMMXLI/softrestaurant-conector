using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace SoftRestaurant.CentralApi;

internal sealed record LinkDeviceRequest(string MachineName, string? AgentVersion, JsonElement? Metadata);
internal sealed record DeviceCredential(Guid InstallationId, string BranchCode, Guid BusinessId, string Token, string? ApiUrl);
internal sealed record ConnectorInstallationView(
    Guid Id,
    string BranchCode,
    string MachineName,
    bool Active,
    string? AgentVersion,
    DateTime CreatedAt,
    DateTime? LinkedAt,
    Guid? LinkedByUserId,
    DateTime? LastSeenAt,
    string? LastIp,
    string? LastUserAgent,
    DateTime? RevokedAt,
    JsonElement Metadata,
    string? LastStatus,
    string? LastError,
    int? PendingBatches,
    DateTime? LastHeartbeatAt,
    DateTime? LastSuccessAt,
    DateTime? LastSyncRequestHandledAt);

internal enum LinkDeviceStatus { Ok, AlreadyActive, BranchNotFound }
internal sealed record LinkDeviceResult(LinkDeviceStatus Status, DeviceCredential? Credential, ConnectorInstallationView? ActiveInstallation)
{
    public static LinkDeviceResult Ok(DeviceCredential credential) => new(LinkDeviceStatus.Ok, credential, null);
    public static LinkDeviceResult AlreadyActive(ConnectorInstallationView active) => new(LinkDeviceStatus.AlreadyActive, null, active);
    public static readonly LinkDeviceResult BranchNotFound = new(LinkDeviceStatus.BranchNotFound, null, null);
}

internal sealed record HeartbeatResult(DateTime? SyncRequestedAt);

/// <summary>
/// Ciclo de vida de un ConnectorInstallation: la identidad propia que central-api emite para un
/// equipo vinculado a una sucursal, completamente separada de la identidad humana que autorizó
/// el vínculo (ver central-api/WebAuthService.cs para la sesión de usuario, y
/// extractor/Config.cs::ProtectedSettings para cómo el agente la persiste vía DPAPI).
///
/// Solo puede haber un ConnectorInstallation activo por sucursal a la vez — lo garantiza el
/// índice único parcial <c>ux_connector_installations_branch_active</c>; esta clase además hace
/// el chequeo antes de intentar el insert para devolver un error claro (409) en vez de depender
/// solo de la excepción de índice.
/// </summary>
internal sealed class ConnectorInstallationRegistry(NpgsqlDataSource dataSource)
{
    private const string InstallationColumns = """
        c.id, b.code, c.machine_name, c.active, c.agent_version,
        c.created_at, c.linked_at, c.linked_by_user_id, c.last_seen_at, c.last_ip, c.last_user_agent,
        c.revoked_at, c.metadata, c.last_status, c.last_error, c.pending_batches, c.last_heartbeat_at,
        c.last_success_at, c.last_sync_request_handled_at
        """;

    private static ConnectorInstallationView ReadInstallation(NpgsqlDataReader reader)
    {
        using var metadata = reader.GetFieldValue<JsonDocument>(12);
        return new ConnectorInstallationView(
            reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetDateTime(5),
            reader.IsDBNull(6) ? null : reader.GetDateTime(6),
            reader.IsDBNull(7) ? null : reader.GetGuid(7),
            reader.IsDBNull(8) ? null : reader.GetDateTime(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetDateTime(11),
            metadata.RootElement.Clone(),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.IsDBNull(15) ? null : reader.GetInt32(15),
            reader.IsDBNull(16) ? null : reader.GetDateTime(16),
            reader.IsDBNull(17) ? null : reader.GetDateTime(17),
            reader.IsDBNull(18) ? null : reader.GetDateTime(18));
    }

    public async Task<ConnectorInstallationView?> GetActiveInstallationAsync(Guid branchId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand($"""
            SELECT {InstallationColumns}
            FROM connector_installations c
            JOIN branches b ON b.id = c.branch_id
            WHERE c.branch_id = $1 AND c.active = true;
            """);
        command.Parameters.AddWithValue(branchId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadInstallation(reader) : null;
    }

    /// <summary>Historial completo de instalaciones de una sucursal (activas y revocadas). Null si la sucursal no existe.</summary>
    public async Task<IReadOnlyList<ConnectorInstallationView>?> GetInstallationsAsync(
        string branchCode, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand($"""
            SELECT {InstallationColumns}
            FROM branches b
            LEFT JOIN connector_installations c ON c.branch_id = b.id
            WHERE b.code = $1
            ORDER BY c.created_at DESC;
            """);
        command.Parameters.AddWithValue(branchCode);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<ConnectorInstallationView>();
        var branchFound = false;
        while (await reader.ReadAsync(ct))
        {
            branchFound = true;
            if (reader.IsDBNull(0)) continue;
            result.Add(ReadInstallation(reader));
        }
        return branchFound ? result : null;
    }

    /// <summary>
    /// Crea la identidad de dispositivo para una sucursal. Falla con <see cref="LinkDeviceStatus.AlreadyActive"/>
    /// si ya hay un conector activo (el llamador debe usar <see cref="ReplaceDeviceAsync"/> en su lugar,
    /// tras una confirmación explícita del usuario) — nunca crea silenciosamente un segundo activo.
    /// </summary>
    public async Task<LinkDeviceResult> LinkDeviceAsync(
        Guid branchId, Guid linkedByUserId, LinkDeviceRequest request, string? apiUrl, HttpContext context, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var lockActive = new NpgsqlCommand(
            "SELECT 1 FROM connector_installations WHERE branch_id = $1 AND active = true FOR UPDATE;",
            connection, transaction))
        {
            lockActive.Parameters.AddWithValue(branchId);
            if (await lockActive.ExecuteScalarAsync(ct) is not null)
            {
                await transaction.RollbackAsync(ct);
                var active = await GetActiveInstallationAsync(branchId, ct);
                return active is null ? LinkDeviceResult.BranchNotFound : LinkDeviceResult.AlreadyActive(active);
            }
        }

        try
        {
            var credential = await InsertInstallationAsync(
                connection, transaction, branchId, linkedByUserId, request, apiUrl, context, ct);
            await transaction.CommitAsync(ct);
            return LinkDeviceResult.Ok(credential);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Carrera perdida contra ux_connector_installations_branch_active: otra vinculación
            // ganó entre el chequeo y el insert. El índice único es el backstop real; esto solo
            // convierte esa colisión en la misma respuesta 409 que el chequeo normal.
            await transaction.RollbackAsync(ct);
            var active = await GetActiveInstallationAsync(branchId, ct);
            return active is null ? LinkDeviceResult.BranchNotFound : LinkDeviceResult.AlreadyActive(active);
        }
    }

    /// <summary>Revoca el conector activo (si hay uno) y crea uno nuevo, atómicamente. El equipo viejo queda rechazado de inmediato.</summary>
    public async Task<DeviceCredential> ReplaceDeviceAsync(
        Guid branchId, Guid linkedByUserId, LinkDeviceRequest request, string? apiUrl, HttpContext context, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var revoke = new NpgsqlCommand("""
            UPDATE connector_installations
            SET active = false, revoked_at = now(), updated_at = now()
            WHERE branch_id = $1 AND active = true;
            """, connection, transaction))
        {
            revoke.Parameters.AddWithValue(branchId);
            await revoke.ExecuteNonQueryAsync(ct);
        }

        var credential = await InsertInstallationAsync(
            connection, transaction, branchId, linkedByUserId, request, apiUrl, context, ct);
        await transaction.CommitAsync(ct);
        return credential;
    }

    private static async Task<DeviceCredential> InsertInstallationAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid branchId, Guid linkedByUserId,
        LinkDeviceRequest request, string? apiUrl, HttpContext context, CancellationToken ct)
    {
        var token = TokenHasher.Generate("sra_conn_");
        var metadata = request.Metadata?.GetRawText() ?? "{}";
        await using var insert = new NpgsqlCommand("""
            INSERT INTO connector_installations
                (branch_id, machine_name, token_hash, agent_version, metadata,
                 linked_by_user_id, last_ip, last_user_agent, last_seen_at)
            SELECT $1, $2, $3, $4, $5::jsonb, $6, $7, $8, now()
            RETURNING id, (SELECT code FROM branches WHERE id = $1), (SELECT business_id FROM branches WHERE id = $1);
            """, connection, transaction);
        insert.Parameters.AddWithValue(branchId);
        insert.Parameters.AddWithValue(request.MachineName);
        insert.Parameters.AddWithValue(TokenHasher.Hash(token));
        insert.Parameters.AddWithValue(
            NpgsqlDbType.Text, request.AgentVersion is null ? DBNull.Value : request.AgentVersion);
        insert.Parameters.AddWithValue(metadata);
        insert.Parameters.AddWithValue(linkedByUserId);
        insert.Parameters.AddWithValue(context.Connection.RemoteIpAddress?.ToString() ?? string.Empty);
        var userAgent = context.Request.Headers.UserAgent.ToString();
        insert.Parameters.AddWithValue(userAgent[..Math.Min(userAgent.Length, 500)]);

        await using var reader = await insert.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new DeviceCredential(reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), token, apiUrl);
    }

    /// <summary>Revocación de operador (admin-web): no valida negocio, el llamador ya está autorizado por AdminAuthenticator.</summary>
    public async Task<bool> RevokeAsync(Guid installationId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE connector_installations
            SET active = false, revoked_at = now(), updated_at = now()
            WHERE id = $1 AND active = true;
            """);
        command.Parameters.AddWithValue(installationId);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    /// <summary>
    /// Revocación self-service (/api/web/*): exige que el conector pertenezca efectivamente a
    /// <paramref name="branchId"/> — nunca confiar en un installationId suelto para decidir el
    /// alcance de la revocación, el llamador ya validó su acceso a esa sucursal, no a "cualquier
    /// instalación cuyo id adivine o reciba".
    /// </summary>
    public async Task<bool> RevokeForBranchAsync(Guid installationId, Guid branchId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE connector_installations
            SET active = false, revoked_at = now(), updated_at = now()
            WHERE id = $1 AND branch_id = $2 AND active = true;
            """);
        command.Parameters.AddWithValue(installationId);
        command.Parameters.AddWithValue(branchId);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    /// <summary>
    /// Registra el latido independiente de un conector (ver extractor/HeartbeatWorker.cs):
    /// actualiza únicamente las columnas de estado reportado, nunca <c>last_seen_at</c> (esa la
    /// mantiene <see cref="AgentAuthenticator"/> en cualquier llamada autenticada). Devuelve la
    /// solicitud de sincronización remota pendiente para la sucursal, si la hay.
    /// </summary>
    public async Task<HeartbeatResult> RecordHeartbeatAsync(
        Guid installationId, Guid branchId, string status, string? error, int pendingBatches,
        DateTime? lastSyncRequestHandledAt, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        await using (var update = new NpgsqlCommand("""
            UPDATE connector_installations
            SET last_status = $3,
                last_error = $4,
                pending_batches = $5,
                last_heartbeat_at = now(),
                last_sync_request_handled_at = $6,
                updated_at = now()
            WHERE id = $1 AND branch_id = $2 AND active = true;
            """, connection))
        {
            update.Parameters.AddWithValue(installationId);
            update.Parameters.AddWithValue(branchId);
            update.Parameters.AddWithValue(status);
            update.Parameters.AddWithValue(NpgsqlDbType.Text, error is null ? DBNull.Value : error);
            update.Parameters.AddWithValue(pendingBatches);
            update.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, lastSyncRequestHandledAt is null ? DBNull.Value : lastSyncRequestHandledAt);
            await update.ExecuteNonQueryAsync(ct);
        }

        await using var select = new NpgsqlCommand(
            "SELECT sync_requested_at FROM branches WHERE id = $1;", connection);
        select.Parameters.AddWithValue(branchId);
        var syncRequestedAt = await select.ExecuteScalarAsync(ct) as DateTime?;
        return new HeartbeatResult(syncRequestedAt);
    }

    /// <summary>Marca la última sincronización exitosa de un conector (llamado por BatchIngestor tras ingerir un lote).</summary>
    public static async Task RecordSuccessAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid installationId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE connector_installations SET last_success_at = now(), updated_at = now() WHERE id = $1;
            """, connection, transaction);
        command.Parameters.AddWithValue(installationId);
        await command.ExecuteNonQueryAsync(ct);
    }
}
