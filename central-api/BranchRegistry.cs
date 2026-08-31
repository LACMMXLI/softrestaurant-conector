using Npgsql;
using NpgsqlTypes;

namespace RestaurantAgent.CentralApi;

internal sealed record BranchCreateRequest(Guid BusinessId, string Code, string Name, string? Timezone);
internal sealed record BranchUpdateRequest(string Name, string? Timezone);
internal sealed record BranchStatusRequest(bool Active);
internal sealed record BranchView(
    Guid Id,
    Guid BusinessId,
    string Code,
    string Name,
    string Timezone,
    bool Active,
    DateTime? LastSyncAt,
    DateTime CreatedAt,
    DateTime? SyncRequestedAt);

/// <summary>
/// CRUD de sucursales (branches). Desde el modelo SaaS, toda sucursal pertenece a un negocio
/// (<see cref="BusinessRegistry"/>) — nunca se crea "suelta". El alta self-service desde
/// /api/web/businesses/{id}/branches y el alta de operador desde /api/admin/branches comparten
/// esta misma clase; la diferencia es solo quién autoriza la llamada.
/// </summary>
internal sealed class BranchRegistry(NpgsqlDataSource dataSource)
{
    private const string BranchColumns =
        "id, business_id, code, name, timezone, active, last_sync_at, created_at, sync_requested_at";

    private static BranchView ReadBranch(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
        reader.GetBoolean(5),
        reader.IsDBNull(6) ? null : reader.GetDateTime(6),
        reader.GetDateTime(7),
        reader.IsDBNull(8) ? null : reader.GetDateTime(8));

    /// <summary>Alta de una sucursal nueva dentro de un negocio. Devuelve null si el código ya existe (conflicto).</summary>
    public async Task<BranchView?> CreateBranchAsync(
        Guid businessId, string code, string name, string timezone, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand($"""
            INSERT INTO branches (business_id, code, name, timezone)
            VALUES ($1, $2, $3, $4)
            ON CONFLICT (code) DO NOTHING
            RETURNING {BranchColumns};
            """);
        command.Parameters.AddWithValue(businessId);
        command.Parameters.AddWithValue(code);
        command.Parameters.AddWithValue(name);
        command.Parameters.AddWithValue(timezone);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadBranch(reader) : null;
    }

    /// <summary>Edita nombre/zona horaria de una sucursal existente. No crea ni reactiva. Devuelve null si no existe.</summary>
    public async Task<BranchView?> UpdateBranchAsync(
        string code, string name, string timezone, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand($"""
            UPDATE branches
            SET name = $2, timezone = $3, updated_at = now()
            WHERE code = $1
            RETURNING {BranchColumns};
            """);
        command.Parameters.AddWithValue(code);
        command.Parameters.AddWithValue(name);
        command.Parameters.AddWithValue(timezone);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadBranch(reader) : null;
    }

    /// <summary>Activa o desactiva una sucursal. Nunca borra filas ni su historial. Devuelve null si no existe.</summary>
    public async Task<BranchView?> SetBranchActiveAsync(
        string code, bool active, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand($"""
            UPDATE branches
            SET active = $2, updated_at = now()
            WHERE code = $1
            RETURNING {BranchColumns};
            """);
        command.Parameters.AddWithValue(code);
        command.Parameters.AddWithValue(active);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadBranch(reader) : null;
    }

    public async Task<BranchView?> GetBranchAsync(string code, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand($"""
            SELECT {BranchColumns}
            FROM branches
            WHERE code = $1;
            """);
        command.Parameters.AddWithValue(code);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadBranch(reader) : null;
    }

    public async Task<IReadOnlyList<BranchView>> GetAllBranchesAsync(CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand($"""
            SELECT {BranchColumns}
            FROM branches
            ORDER BY name;
            """);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<BranchView>();
        while (await reader.ReadAsync(ct)) result.Add(ReadBranch(reader));
        return result;
    }

    /// <summary>Sucursales activas de un negocio. Usada por /api/web/businesses/{id}/branches (self-service, ya autorizado por el caller).</summary>
    public async Task<IReadOnlyList<BranchView>> GetBranchesForBusinessAsync(Guid businessId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand($"""
            SELECT {BranchColumns}
            FROM branches
            WHERE business_id = $1
            ORDER BY name;
            """);
        command.Parameters.AddWithValue(businessId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<BranchView>();
        while (await reader.ReadAsync(ct)) result.Add(ReadBranch(reader));
        return result;
    }

    /// <summary>Sucursal activa: cuál era la última solicitud de sync remota, si hay alguna.</summary>
    public async Task<DateTime?> GetSyncRequestedAtAsync(string branchCode, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT sync_requested_at FROM branches WHERE code = $1;
            """);
        command.Parameters.AddWithValue(branchCode);
        return await command.ExecuteScalarAsync(ct) as DateTime?;
    }

    /// <summary>
    /// Marca una solicitud de sincronización remota para la sucursal. Mecanismo simple (un solo
    /// timestamp, no una cola de comandos): el agente la recoge en su siguiente latido.
    /// Devuelve null si la sucursal no existe o está inactiva.
    /// </summary>
    public async Task<BranchView?> RequestSyncAsync(string branchCode, Guid? requestedByUserId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand($"""
            UPDATE branches
            SET sync_requested_at = now(), sync_requested_by = $2, updated_at = now()
            WHERE code = $1 AND active = true
            RETURNING {BranchColumns};
            """);
        command.Parameters.AddWithValue(branchCode);
        command.Parameters.AddWithValue(NpgsqlDbType.Uuid, requestedByUserId is null ? DBNull.Value : requestedByUserId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadBranch(reader) : null;
    }
}
