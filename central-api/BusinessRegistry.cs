using System.Text.RegularExpressions;
using Npgsql;

namespace SoftRestaurant.CentralApi;

internal sealed record BusinessCreateRequest(string Name);
internal sealed record BusinessView(Guid Id, string Name, string Slug, bool Active, DateTime CreatedAt);
internal sealed record BusinessMembershipView(Guid Id, string Name, string Slug, bool Active, DateTime CreatedAt, string Role);

/// <summary>
/// Capa "negocio" entre la cuenta y la sucursal (Account/User → Business → Branch →
/// ConnectorInstallation). Una cuenta puede pertenecer a varios negocios; el permiso real
/// (OWNER/MANAGER/VIEWER) vive en <c>business_members</c>, no en <c>app_users.role</c> (que solo
/// distingue SUPERADMIN de USER — ver central-api/schema.sql).
/// </summary>
internal sealed partial class BusinessRegistry(NpgsqlDataSource dataSource)
{
    /// <summary>Negocios de los que el usuario es miembro explícito, con su rol en cada uno. Nunca usa el atajo SUPERADMIN (ver BusinessAccess).</summary>
    public async Task<IReadOnlyList<BusinessMembershipView>> GetMyBusinessesAsync(Guid userId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT b.id, b.name, b.slug, b.active, b.created_at, bm.role
            FROM business_members bm
            JOIN businesses b ON b.id = bm.business_id
            WHERE bm.user_id = $1
            ORDER BY b.name;
            """);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<BusinessMembershipView>();
        while (await reader.ReadAsync(ct))
        {
            result.Add(new BusinessMembershipView(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3),
                reader.GetDateTime(4), reader.GetString(5)));
        }
        return result;
    }

    public async Task<IReadOnlyList<Guid>> GetMemberBusinessIdsAsync(Guid userId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT business_id FROM business_members WHERE user_id = $1;");
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<Guid>();
        while (await reader.ReadAsync(ct)) result.Add(reader.GetGuid(0));
        return result;
    }

    /// <summary>Rol del usuario en el negocio, o null si no es miembro.</summary>
    public async Task<string?> GetMemberRoleAsync(Guid businessId, Guid userId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT role FROM business_members WHERE business_id = $1 AND user_id = $2;
            """);
        command.Parameters.AddWithValue(businessId);
        command.Parameters.AddWithValue(userId);
        return await command.ExecuteScalarAsync(ct) as string;
    }

    /// <summary>Rol del usuario en el negocio dueño de esta sucursal, o null si no tiene acceso.</summary>
    public async Task<string?> GetMemberRoleForBranchAsync(string branchCode, Guid userId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT bm.role
            FROM branches b
            JOIN business_members bm ON bm.business_id = b.business_id
            WHERE b.code = $1 AND bm.user_id = $2;
            """);
        command.Parameters.AddWithValue(branchCode);
        command.Parameters.AddWithValue(userId);
        return await command.ExecuteScalarAsync(ct) as string;
    }

    /// <summary>Crea un negocio y hace OWNER a quien lo crea, en una sola transacción.</summary>
    public async Task<BusinessMembershipView> CreateBusinessAsync(Guid creatorUserId, string name, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var slug = await ReserveUniqueSlugAsync(name, connection, transaction, ct);

        Guid businessId;
        DateTime createdAt;
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO businesses (name, slug) VALUES ($1, $2)
            RETURNING id, created_at;
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue(name);
            insert.Parameters.AddWithValue(slug);
            await using var reader = await insert.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            businessId = reader.GetGuid(0);
            createdAt = reader.GetDateTime(1);
        }

        await using (var member = new NpgsqlCommand("""
            INSERT INTO business_members (business_id, user_id, role) VALUES ($1, $2, 'OWNER');
            """, connection, transaction))
        {
            member.Parameters.AddWithValue(businessId);
            member.Parameters.AddWithValue(creatorUserId);
            await member.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return new BusinessMembershipView(businessId, name, slug, true, createdAt, "OWNER");
    }

    /// <summary>Todos los negocios (panel de operador, SUPERADMIN). No filtra por membresía.</summary>
    public async Task<IReadOnlyList<BusinessView>> GetAllBusinessesAsync(CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT id, name, slug, active, created_at FROM businesses ORDER BY name;
            """);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<BusinessView>();
        while (await reader.ReadAsync(ct))
        {
            result.Add(new BusinessView(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetDateTime(4)));
        }
        return result;
    }

    /// <summary>Otorga o actualiza la membresía de un usuario en varios negocios (panel de operador). Idempotente.</summary>
    public async Task AssignMembershipsAsync(Guid userId, IReadOnlyList<Guid> businessIds, string role, CancellationToken ct)
    {
        if (businessIds.Count == 0) return;
        await using var command = dataSource.CreateCommand("""
            INSERT INTO business_members (business_id, user_id, role)
            SELECT unnest($1::uuid[]), $2, $3
            ON CONFLICT (business_id, user_id) DO UPDATE SET role = excluded.role;
            """);
        command.Parameters.AddWithValue(businessIds.ToArray());
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(role);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> RemoveMembershipAsync(Guid userId, Guid businessId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(
            "DELETE FROM business_members WHERE user_id = $1 AND business_id = $2;");
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(businessId);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    /// <summary>Genera un slug único a partir del nombre, reintentando con un sufijo aleatorio si colisiona.</summary>
    private static async Task<string> ReserveUniqueSlugAsync(
        string name, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        var baseSlug = Slugify(name);
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var candidate = attempt == 0
                ? baseSlug
                : $"{baseSlug}-{Convert.ToHexString(RandomBytes(3)).ToLowerInvariant()}";
            await using var check = new NpgsqlCommand(
                "SELECT 1 FROM businesses WHERE slug = $1;", connection, transaction);
            check.Parameters.AddWithValue(candidate);
            if (await check.ExecuteScalarAsync(ct) is null) return candidate;
        }
        throw new InvalidOperationException("No se pudo generar un slug único para el negocio.");
    }

    private static byte[] RandomBytes(int count)
    {
        var bytes = new byte[count];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    private static string Slugify(string name)
    {
        var lowered = name.Trim().ToLowerInvariant();
        var slug = NonSlugCharacters().Replace(lowered, "-").Trim('-');
        if (string.IsNullOrEmpty(slug)) slug = "negocio";
        return slug.Length > 80 ? slug[..80].Trim('-') : slug;
    }

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSlugCharacters();
}
