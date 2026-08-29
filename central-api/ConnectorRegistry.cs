using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace SoftRestaurant.CentralApi;

internal sealed record BranchRequest(string Name, string? Timezone);
internal sealed record ActivationKeyRequest(int? ExpiresInMinutes, string? Note);
internal sealed record ActivateConnectorRequest(
    string ActivationKey,
    string MachineName,
    string? AgentVersion,
    JsonElement? Metadata);
internal sealed record ConnectorActivation(Guid ConnectorId, string BranchCode, string Token);
internal sealed record ActivationKeyResult(Guid Id, string ActivationKey, DateTime ExpiresAt);
internal sealed record ConnectorView(
    Guid Id,
    string BranchCode,
    string MachineName,
    bool Active,
    string? AgentVersion,
    DateTime CreatedAt,
    DateTime? LastSeenAt,
    string? LastIp,
    string? LastUserAgent,
    DateTime? RevokedAt,
    DateTime? TokenRotatedAt,
    JsonElement Metadata);

internal sealed class ConnectorRegistry(NpgsqlDataSource dataSource)
{
    public async Task<object> UpsertBranchAsync(
        string code, string name, string timezone, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            INSERT INTO branches (code, name, timezone, legacy_auth_enabled)
            VALUES ($1, $2, $3, false)
            ON CONFLICT (code) DO UPDATE
            SET name = excluded.name,
                timezone = excluded.timezone,
                updated_at = now()
            RETURNING id, code, name, timezone, active, legacy_auth_enabled, created_at;
            """);
        command.Parameters.AddWithValue(code);
        command.Parameters.AddWithValue(name);
        command.Parameters.AddWithValue(timezone);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new
        {
            id = reader.GetGuid(0),
            code = reader.GetString(1),
            name = reader.GetString(2),
            timezone = reader.GetString(3),
            active = reader.GetBoolean(4),
            legacyAuthEnabled = reader.GetBoolean(5),
            createdAt = reader.GetDateTime(6)
        };
    }

    public async Task<ActivationKeyResult?> CreateActivationKeyAsync(
        string branchCode, int expiresInMinutes, string? note, CancellationToken ct)
    {
        var activationKey = TokenHasher.Generate("sra_act_");
        await using var command = dataSource.CreateCommand("""
            INSERT INTO connector_activation_keys (branch_id, key_hash, expires_at, note)
            SELECT id, $2, now() + make_interval(mins => $3), $4
            FROM branches
            WHERE code = $1 AND active = true
            RETURNING id, expires_at;
            """);
        command.Parameters.AddWithValue(branchCode);
        command.Parameters.AddWithValue(TokenHasher.Hash(activationKey));
        command.Parameters.AddWithValue(expiresInMinutes);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, note is null ? DBNull.Value : note);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new ActivationKeyResult(reader.GetGuid(0), activationKey, reader.GetDateTime(1));
    }

    public async Task<ConnectorActivation?> ActivateAsync(
        ActivateConnectorRequest request, HttpContext context, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using var find = new NpgsqlCommand("""
            SELECT ak.id, b.id, b.code
            FROM connector_activation_keys ak
            JOIN branches b ON b.id = ak.branch_id
            WHERE ak.key_hash = $1
              AND ak.used_at IS NULL
              AND ak.expires_at > now()
              AND b.active = true
            FOR UPDATE OF ak;
            """, connection, transaction);
        find.Parameters.AddWithValue(TokenHasher.Hash(request.ActivationKey));
        await using var reader = await find.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            await reader.DisposeAsync();
            await transaction.RollbackAsync(ct);
            return null;
        }

        var activationId = reader.GetGuid(0);
        var branchId = reader.GetGuid(1);
        var branchCode = reader.GetString(2);
        await reader.DisposeAsync();

        var token = TokenHasher.Generate("sra_conn_");
        var metadata = request.Metadata?.GetRawText() ?? "{}";
        await using var insert = new NpgsqlCommand("""
            INSERT INTO connectors
                (branch_id, machine_name, token_hash, agent_version, metadata,
                 last_ip, last_user_agent, last_seen_at)
            VALUES ($1, $2, $3, $4, $5::jsonb, $6, $7, now())
            RETURNING id;
            """, connection, transaction);
        insert.Parameters.AddWithValue(branchId);
        insert.Parameters.AddWithValue(request.MachineName);
        insert.Parameters.AddWithValue(TokenHasher.Hash(token));
        insert.Parameters.AddWithValue(
            NpgsqlDbType.Text, request.AgentVersion is null ? DBNull.Value : request.AgentVersion);
        insert.Parameters.AddWithValue(metadata);
        insert.Parameters.AddWithValue(context.Connection.RemoteIpAddress?.ToString() ?? string.Empty);
        var userAgent = context.Request.Headers.UserAgent.ToString();
        insert.Parameters.AddWithValue(userAgent[..Math.Min(userAgent.Length, 500)]);
        var connectorId = (Guid)(await insert.ExecuteScalarAsync(ct)
            ?? throw new InvalidOperationException("No se creó el conector."));

        await using var consume = new NpgsqlCommand("""
            UPDATE connector_activation_keys
            SET used_at = now(), used_by_connector_id = $2
            WHERE id = $1;
            """, connection, transaction);
        consume.Parameters.AddWithValue(activationId);
        consume.Parameters.AddWithValue(connectorId);
        await consume.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return new ConnectorActivation(connectorId, branchCode, token);
    }

    public async Task<IReadOnlyList<ConnectorView>?> GetConnectorsAsync(
        string branchCode, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT c.id, b.code, c.machine_name, c.active, c.agent_version,
                   c.created_at, c.last_seen_at, c.last_ip, c.last_user_agent,
                   c.revoked_at, c.token_rotated_at, c.metadata
            FROM branches b
            LEFT JOIN connectors c ON c.branch_id = b.id
            WHERE b.code = $1
            ORDER BY c.created_at DESC;
            """);
        command.Parameters.AddWithValue(branchCode);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<ConnectorView>();
        var branchFound = false;
        while (await reader.ReadAsync(ct))
        {
            branchFound = true;
            if (reader.IsDBNull(0)) continue;
            using var metadata = reader.GetFieldValue<JsonDocument>(11);
            result.Add(new ConnectorView(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetDateTime(5),
                reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                metadata.RootElement.Clone()));
        }
        return branchFound ? result : null;
    }

    public async Task<bool> RevokeAsync(Guid connectorId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE connectors
            SET active = false, revoked_at = now(), updated_at = now()
            WHERE id = $1 AND active = true;
            """);
        command.Parameters.AddWithValue(connectorId);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<object?> RotateTokenAsync(Guid connectorId, CancellationToken ct)
    {
        var token = TokenHasher.Generate("sra_conn_");
        await using var command = dataSource.CreateCommand("""
            UPDATE connectors c
            SET token_hash = $2, token_rotated_at = now(), updated_at = now()
            FROM branches b
            WHERE c.id = $1 AND c.active = true AND b.id = c.branch_id
            RETURNING b.code;
            """);
        command.Parameters.AddWithValue(connectorId);
        command.Parameters.AddWithValue(TokenHasher.Hash(token));
        var branchCode = await command.ExecuteScalarAsync(ct) as string;
        return branchCode is null ? null : new { connectorId, branchCode, token };
    }

    public async Task<bool> DisableLegacyAsync(string branchCode, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE branches
            SET legacy_auth_enabled = false, updated_at = now()
            WHERE code = $1;
            """);
        command.Parameters.AddWithValue(branchCode);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }
}
