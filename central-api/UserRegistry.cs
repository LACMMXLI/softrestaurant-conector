using Npgsql;

namespace RestaurantAgent.CentralApi;

internal sealed record UserView(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    bool Active,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    int BusinessCount,
    SubscriptionView Subscription);

internal sealed record UserBusinessView(Guid BusinessId, string Name, string Slug, bool Active, string Role);

internal sealed record UserDetailView(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    bool Active,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    IReadOnlyList<UserBusinessView> Businesses,
    SubscriptionView Subscription);

internal enum UserMutationStatus { Ok, NotFound, BlockedLastSuperAdmin }

internal sealed record UserMutationResult(UserMutationStatus Status, UserDetailView? User)
{
    public static UserMutationResult NotFound { get; } = new(UserMutationStatus.NotFound, null);
    public static UserMutationResult BlockedLastSuperAdmin { get; } = new(UserMutationStatus.BlockedLastSuperAdmin, null);
    public static UserMutationResult Ok(UserDetailView user) => new(UserMutationStatus.Ok, user);
}

internal sealed record UserCreateRequest(string Email, string DisplayName, string Password, string Role);
internal sealed record UserUpdateRequest(string DisplayName, string Role);
internal sealed record UserStatusRequest(bool Active);
internal sealed record UserPasswordResetRequest(string Password);
internal sealed record UserBusinessAssignRequest(IReadOnlyList<Guid> BusinessIds, string Role);

/// <summary>
/// CRUD de cuentas (app_users) y de su membresía de negocio (business_members), para los
/// endpoints /api/admin/users/*. Ninguna operación borra una fila de app_users: el historial y
/// la auditoría (audit_log, last_login_at) se conservan siempre; "eliminar" una cuenta es
/// desactivarla (<see cref="SetUserActiveAsync"/>). El rol de cuenta (SUPERADMIN/USER) y el rol
/// de negocio (OWNER/MANAGER/VIEWER, en business_members) son conceptos separados desde el
/// modelo SaaS — ver central-api/schema.sql.
/// </summary>
internal sealed class UserRegistry(NpgsqlDataSource dataSource, WebAuthService authService, SubscriptionRegistry subscriptions)
{
    public async Task<bool> CreateFirstSuperAdminAsync(
        string email, string displayName, string password, CancellationToken ct)
    {
        var normalizedEmail = WebAuthService.NormalizeEmail(email);
        var candidate = new DashboardUser(Guid.Empty, normalizedEmail, displayName, "SUPERADMIN");
        var passwordHash = authService.HashPassword(candidate, password);

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var lockCommand = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended('restaurant-agent:first-superadmin', 0));",
            connection, transaction))
        {
            await lockCommand.ExecuteScalarAsync(ct);
        }

        await using (var exists = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM app_users WHERE role = 'SUPERADMIN');",
            connection, transaction))
        {
            if ((bool)(await exists.ExecuteScalarAsync(ct) ?? false))
            {
                await transaction.RollbackAsync(ct);
                return false;
            }
        }

        await using var insert = new NpgsqlCommand("""
            INSERT INTO app_users (email, display_name, password_hash, role)
            VALUES ($1, $2, $3, 'SUPERADMIN');
            """, connection, transaction);
        insert.Parameters.AddWithValue(normalizedEmail);
        insert.Parameters.AddWithValue(displayName);
        insert.Parameters.AddWithValue(passwordHash);
        await insert.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<UserView>> GetAllUsersAsync(CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT u.id, u.email, u.display_name, u.role, u.active, u.last_login_at, u.created_at,
                   COUNT(bm.business_id), u.subscription_plan, u.trial_ends_at, u.paid_until, u.subscription_suspended
            FROM app_users u
            LEFT JOIN business_members bm ON bm.user_id = u.id
            GROUP BY u.id
            ORDER BY u.display_name, u.email;
            """);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<UserView>();
        while (await reader.ReadAsync(ct))
        {
            result.Add(new UserView(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetBoolean(4), reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                reader.GetDateTime(6), checked((int)reader.GetInt64(7)),
                SubscriptionPolicy.Evaluate(reader.GetString(8), reader.GetDateTime(9),
                    reader.IsDBNull(10) ? null : reader.GetDateTime(10), reader.GetBoolean(11), DateTime.UtcNow)));
        }
        return result;
    }

    public async Task<UserDetailView?> GetUserAsync(Guid id, CancellationToken ct)
    {
        UserDetailView? user;
        await using (var command = dataSource.CreateCommand("""
            SELECT id, email, display_name, role, active, last_login_at, created_at
            FROM app_users
            WHERE id = $1;
            """))
        {
            command.Parameters.AddWithValue(id);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            user = new UserDetailView(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetBoolean(4), reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                reader.GetDateTime(6), [], (await subscriptions.GetAsync(id, ct))!);
        }

        var businesses = new List<UserBusinessView>();
        await using (var command = dataSource.CreateCommand("""
            SELECT b.id, b.name, b.slug, b.active, bm.role
            FROM business_members bm
            JOIN businesses b ON b.id = bm.business_id
            WHERE bm.user_id = $1
            ORDER BY b.name;
            """))
        {
            command.Parameters.AddWithValue(id);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                businesses.Add(new UserBusinessView(
                    reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetString(4)));
        }

        return user with { Businesses = businesses };
    }

    /// <summary>Crea la cuenta (sin membresía de negocio inicial). Devuelve null si el correo ya existe.</summary>
    public async Task<UserDetailView?> CreateUserAsync(
        string email, string displayName, string password, string role, CancellationToken ct)
    {
        var normalizedEmail = WebAuthService.NormalizeEmail(email);
        var passwordHash = authService.HashPassword(
            new DashboardUser(Guid.Empty, normalizedEmail, displayName, role), password);

        await using var command = dataSource.CreateCommand("""
            INSERT INTO app_users (email, display_name, password_hash, role)
            VALUES ($1, $2, $3, $4)
            ON CONFLICT DO NOTHING
            RETURNING id;
            """);
        command.Parameters.AddWithValue(normalizedEmail);
        command.Parameters.AddWithValue(displayName);
        command.Parameters.AddWithValue(passwordHash);
        command.Parameters.AddWithValue(role);
        return await command.ExecuteScalarAsync(ct) is Guid id ? await GetUserAsync(id, ct) : null;
    }

    /// <summary>Edita nombre y rol de cuenta. Bloqueado si dejaría al sistema sin SUPERADMIN activo.</summary>
    public async Task<UserMutationResult> UpdateUserAsync(
        Guid id, string displayName, string role, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var current = await LockUserAsync(id, connection, transaction, ct);
        if (current is null)
        {
            await transaction.RollbackAsync(ct);
            return UserMutationResult.NotFound;
        }

        var otherActiveSuperAdmins = await CountOtherActiveSuperAdminsAsync(id, connection, transaction, ct);
        if (SuperAdminGuard.WouldRemoveLastActiveSuperAdmin(
                current.Value.Role, current.Value.Active, role, newActive: null, otherActiveSuperAdmins))
        {
            await transaction.RollbackAsync(ct);
            return UserMutationResult.BlockedLastSuperAdmin;
        }

        await using (var update = new NpgsqlCommand("""
            UPDATE app_users SET display_name = $2, role = $3, updated_at = now() WHERE id = $1;
            """, connection, transaction))
        {
            update.Parameters.AddWithValue(id);
            update.Parameters.AddWithValue(displayName);
            update.Parameters.AddWithValue(role);
            await update.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return UserMutationResult.Ok((await GetUserAsync(id, ct))!);
    }

    /// <summary>Activa/desactiva. Bloqueado si desactivaría al último SUPERADMIN activo.</summary>
    public async Task<UserMutationResult> SetUserActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var current = await LockUserAsync(id, connection, transaction, ct);
        if (current is null)
        {
            await transaction.RollbackAsync(ct);
            return UserMutationResult.NotFound;
        }

        var otherActiveSuperAdmins = await CountOtherActiveSuperAdminsAsync(id, connection, transaction, ct);
        if (SuperAdminGuard.WouldRemoveLastActiveSuperAdmin(
                current.Value.Role, current.Value.Active, newRole: null, active, otherActiveSuperAdmins))
        {
            await transaction.RollbackAsync(ct);
            return UserMutationResult.BlockedLastSuperAdmin;
        }

        await using (var update = new NpgsqlCommand(
            "UPDATE app_users SET active = $2, updated_at = now() WHERE id = $1;", connection, transaction))
        {
            update.Parameters.AddWithValue(id);
            update.Parameters.AddWithValue(active);
            await update.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);

        // Corta cualquier sesión ya abierta de inmediato: no solo hacia adelante (el login y
        // AuthenticateAsync ya filtran por active=true), sino también las pestañas abiertas.
        if (!active) await authService.InvalidateSessionsAsync(id, ct);

        return UserMutationResult.Ok((await GetUserAsync(id, ct))!);
    }

    /// <summary>Restablece la contraseña con el mismo hasher que login/bootstrap y revoca sesiones.</summary>
    public async Task<UserDetailView?> ResetPasswordAsync(Guid id, string newPassword, CancellationToken ct)
    {
        var existing = await GetUserAsync(id, ct);
        if (existing is null) return null;

        var passwordHash = authService.HashPassword(
            new DashboardUser(id, existing.Email, existing.DisplayName, existing.Role), newPassword);

        await using (var command = dataSource.CreateCommand(
            "UPDATE app_users SET password_hash = $2, updated_at = now() WHERE id = $1;"))
        {
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(passwordHash);
            await command.ExecuteNonQueryAsync(ct);
        }

        await authService.InvalidateSessionsAsync(id, ct);
        return existing;
    }

    /// <summary>Otorga/actualiza membresía en uno o más negocios (mismo rol para todos). Null si el usuario no existe.</summary>
    public async Task<UserDetailView?> AssignBusinessesAsync(
        Guid id, IReadOnlyList<Guid> businessIds, string role, BusinessRegistry businesses, CancellationToken ct)
    {
        await using var exists = dataSource.CreateCommand("SELECT 1 FROM app_users WHERE id = $1;");
        exists.Parameters.AddWithValue(id);
        if (await exists.ExecuteScalarAsync(ct) is null) return null;

        await businesses.AssignMembershipsAsync(id, businessIds, role, ct);
        return await GetUserAsync(id, ct);
    }

    public async Task<bool> RemoveBusinessAsync(Guid id, Guid businessId, BusinessRegistry businesses, CancellationToken ct) =>
        await businesses.RemoveMembershipAsync(id, businessId, ct);

    private static async Task<(string Role, bool Active)?> LockUserAsync(
        Guid id, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT role, active FROM app_users WHERE id = $1 FOR UPDATE;", connection, transaction);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? (reader.GetString(0), reader.GetBoolean(1)) : null;
    }

    /// <summary>
    /// Cuenta y bloquea (FOR UPDATE) las cuentas SUPERADMIN activas distintas de <paramref name="excludingId"/>,
    /// dentro de la misma transacción que la fila objetivo ya bloqueada por <see cref="LockUserAsync"/>, para
    /// que la comprobación del último SUPERADMIN sea atómica frente a cambios concurrentes.
    /// </summary>
    private static async Task<int> CountOtherActiveSuperAdminsAsync(
        Guid excludingId, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT COUNT(*) FROM (
                SELECT id FROM app_users
                WHERE role = 'SUPERADMIN' AND active = true AND id <> $1
                FOR UPDATE
            ) locked;
            """, connection, transaction);
        command.Parameters.AddWithValue(excludingId);
        return checked((int)(long)(await command.ExecuteScalarAsync(ct) ?? 0L));
    }
}
