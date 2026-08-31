using Microsoft.AspNetCore.Identity;
using Npgsql;

namespace SoftRestaurant.CentralApi;

internal sealed record DashboardUser(Guid Id, string Email, string DisplayName, string Role)
{
    public bool IsSuperAdmin => string.Equals(Role, "SUPERADMIN", StringComparison.Ordinal);
}

internal sealed record DashboardLoginResult(
    string Token,
    DashboardUser User,
    DateTime ExpiresAtUtc);

internal enum RegisterStatus { Ok, EmailTaken }
internal sealed record RegisterResult(RegisterStatus Status, DashboardLoginResult? Login)
{
    public static RegisterResult Ok(DashboardLoginResult login) => new(RegisterStatus.Ok, login);
    public static readonly RegisterResult EmailTaken = new(RegisterStatus.EmailTaken, null);
}

internal sealed class WebAuthService(NpgsqlDataSource dataSource, ApiOptions options)
{
    public const string CookieName = "sr_dashboard_session";

    private readonly PasswordHasher<DashboardUser> passwordHasher = new();

    /// <summary>
    /// Hashea una contraseña con exactamente el mismo mecanismo (ASP.NET Core Identity
    /// <see cref="PasswordHasher{TUser}"/>) que usan el login y el bootstrap de owner/admin.
    /// La usa UserRegistry al crear cuentas o restablecer contraseñas desde el panel admin.
    /// </summary>
    public string HashPassword(DashboardUser candidate, string password) =>
        passwordHasher.HashPassword(candidate, password);

    /// <summary>
    /// Revoca todas las sesiones activas de un usuario. Se llama al desactivar una cuenta o
    /// restablecer su contraseña, para que un cambio administrativo tenga efecto inmediato
    /// en vez de esperar a que expire la cookie.
    /// </summary>
    public async Task InvalidateSessionsAsync(Guid userId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(
            "DELETE FROM app_sessions WHERE user_id = $1;");
        command.Parameters.AddWithValue(userId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task EnsureBootstrapOwnerAsync(CancellationToken ct)
    {
        // "OWNER" ya no es un rol de cuenta (app_users.role solo admite SUPERADMIN/USER — el
        // permiso real vive en business_members). La cuenta bootstrap se crea como USER y, si
        // existe el negocio bootstrap (ver DbInitializer, creado solo cuando hay
        // BOOTSTRAP_BRANCH_CODE), se le otorga membresía OWNER ahí para no romper el flujo de
        // instalaciones piloto existentes.
        var ownerId = await EnsureBootstrapUserAsync(options.DashboardOwnerEmail, options.DashboardOwnerPassword, "USER", ct);
        await EnsureBootstrapUserAsync(options.DashboardAdminEmail, options.DashboardAdminPassword, "SUPERADMIN", ct);

        if (ownerId is not { } id) return;
        await using var membership = dataSource.CreateCommand("""
            INSERT INTO business_members (business_id, user_id, role)
            SELECT id, $1, 'OWNER' FROM businesses WHERE slug = 'negocio-principal'
            ON CONFLICT (business_id, user_id) DO NOTHING;
            """);
        membership.Parameters.AddWithValue(id);
        await membership.ExecuteNonQueryAsync(ct);
    }

    private async Task<Guid?> EnsureBootstrapUserAsync(
        string? email, string? password, string role, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return null;

        var normalizedEmail = NormalizeEmail(email);
        var candidate = new DashboardUser(Guid.Empty, normalizedEmail, normalizedEmail, role);
        var passwordHash = passwordHasher.HashPassword(candidate, password);

        await using var command = dataSource.CreateCommand("""
            INSERT INTO app_users (email, display_name, password_hash, role)
            VALUES ($1, $1, $2, $3)
            ON CONFLICT DO NOTHING
            RETURNING id;
            """);
        command.Parameters.AddWithValue(normalizedEmail);
        command.Parameters.AddWithValue(passwordHash);
        command.Parameters.AddWithValue(role);
        var result = await command.ExecuteScalarAsync(ct);
        if (result is Guid id) return id;

        await using var lookup = dataSource.CreateCommand(
            "SELECT id FROM app_users WHERE lower(email) = $1;");
        lookup.Parameters.AddWithValue(normalizedEmail);
        return await lookup.ExecuteScalarAsync(ct) as Guid?;
    }

    /// <summary>
    /// Autorregistro público: crea la cuenta con rol USER (nunca SUPERADMIN) y abre sesión de
    /// inmediato, igual que login. No crea ningún negocio — el usuario crea el primero desde el
    /// dashboard con <c>POST /api/web/businesses</c>.
    /// </summary>
    public async Task<RegisterResult> RegisterAsync(
        string email, string password, string displayName, string? ip, string? userAgent, CancellationToken ct)
    {
        var normalizedEmail = NormalizeEmail(email);
        var candidate = new DashboardUser(Guid.Empty, normalizedEmail, displayName, "USER");
        var passwordHash = passwordHasher.HashPassword(candidate, password);

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        DashboardUser user;
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO app_users (email, display_name, password_hash, role)
            VALUES ($1, $2, $3, 'USER')
            ON CONFLICT DO NOTHING
            RETURNING id;
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue(normalizedEmail);
            insert.Parameters.AddWithValue(displayName);
            insert.Parameters.AddWithValue(passwordHash);
            if (await insert.ExecuteScalarAsync(ct) is not Guid id)
            {
                await transaction.RollbackAsync(ct);
                return RegisterResult.EmailTaken;
            }
            user = new DashboardUser(id, normalizedEmail, displayName, "USER");
        }

        var login = await CreateSessionAsync(connection, transaction, user, ip, userAgent, "REGISTER", ct);
        await transaction.CommitAsync(ct);
        return RegisterResult.Ok(login);
    }

    public async Task<DashboardLoginResult?> LoginAsync(
        string email,
        string password,
        string? ip,
        string? userAgent,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return null;

        var normalizedEmail = NormalizeEmail(email);
        DashboardUser? user = null;
        string? passwordHash = null;

        await using (var lookup = dataSource.CreateCommand("""
            SELECT id, email, display_name, role, password_hash
            FROM app_users
            WHERE lower(email) = $1 AND active = true;
            """))
        {
            lookup.Parameters.AddWithValue(normalizedEmail);
            await using var reader = await lookup.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                user = new DashboardUser(
                    reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
                passwordHash = reader.GetString(4);
            }
        }

        if (user is null || passwordHash is null) return null;
        var verification = passwordHasher.VerifyHashedPassword(user, passwordHash, password);
        if (verification == PasswordVerificationResult.Failed) return null;

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            await using var rehash = new NpgsqlCommand("""
                UPDATE app_users SET password_hash = $1, updated_at = now() WHERE id = $2;
                """, connection, transaction);
            rehash.Parameters.AddWithValue(passwordHasher.HashPassword(user, password));
            rehash.Parameters.AddWithValue(user.Id);
            await rehash.ExecuteNonQueryAsync(ct);
        }

        var login = await CreateSessionAsync(connection, transaction, user, ip, userAgent, "LOGIN", ct);
        await transaction.CommitAsync(ct);
        return login;
    }

    /// <summary>
    /// Núcleo compartido de login y registro: limpia sesiones vencidas, inserta la sesión nueva,
    /// actualiza <c>last_login_at</c> y deja constancia en <c>audit_log</c>. Antes duplicado
    /// entre LoginAsync y (el ahora existente) RegisterAsync.
    /// </summary>
    private async Task<DashboardLoginResult> CreateSessionAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, DashboardUser user,
        string? ip, string? userAgent, string auditEventType, CancellationToken ct)
    {
        var token = TokenHasher.Generate("srd_session_");
        var expiresAt = DateTime.UtcNow.AddHours(options.DashboardSessionHours);

        await using (var cleanup = new NpgsqlCommand(
            "DELETE FROM app_sessions WHERE expires_at <= now();", connection, transaction))
        {
            await cleanup.ExecuteNonQueryAsync(ct);
        }

        await using (var session = new NpgsqlCommand("""
            INSERT INTO app_sessions (token_hash, user_id, expires_at, ip, user_agent)
            VALUES ($1, $2, $3, $4, $5);
            """, connection, transaction))
        {
            session.Parameters.AddWithValue(TokenHasher.Hash(token));
            session.Parameters.AddWithValue(user.Id);
            session.Parameters.AddWithValue(expiresAt);
            session.Parameters.AddWithValue((object?)Limit(ip, 200) ?? DBNull.Value);
            session.Parameters.AddWithValue((object?)Limit(userAgent, 500) ?? DBNull.Value);
            await session.ExecuteNonQueryAsync(ct);
        }

        await using (var updateUser = new NpgsqlCommand("""
            UPDATE app_users SET last_login_at = now(), updated_at = now() WHERE id = $1;
            """, connection, transaction))
        {
            updateUser.Parameters.AddWithValue(user.Id);
            await updateUser.ExecuteNonQueryAsync(ct);
        }

        await using (var audit = new NpgsqlCommand(
            "INSERT INTO audit_log (user_id, event_type, ip) VALUES ($1, $2, $3);", connection, transaction))
        {
            audit.Parameters.AddWithValue(user.Id);
            audit.Parameters.AddWithValue(auditEventType);
            audit.Parameters.AddWithValue((object?)Limit(ip, 200) ?? DBNull.Value);
            await audit.ExecuteNonQueryAsync(ct);
        }

        return new DashboardLoginResult(token, user, expiresAt);
    }

    public async Task<DashboardUser?> AuthenticateAsync(HttpContext context, CancellationToken ct)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var token) ||
            string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        await using var command = dataSource.CreateCommand("""
            SELECT u.id, u.email, u.display_name, u.role
            FROM app_sessions s
            JOIN app_users u ON u.id = s.user_id
            WHERE s.token_hash = $1
              AND s.expires_at > now()
              AND u.active = true;
            """);
        command.Parameters.AddWithValue(TokenHasher.Hash(token));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new DashboardUser(
            reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
    }

    public async Task LogoutAsync(HttpContext context, CancellationToken ct)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var token) ||
            string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        await using var command = dataSource.CreateCommand(
            "DELETE FROM app_sessions WHERE token_hash = $1;");
        command.Parameters.AddWithValue(TokenHasher.Hash(token));
        await command.ExecuteNonQueryAsync(ct);
    }

    public static void SetSessionCookie(HttpContext context, DashboardLoginResult login)
    {
        context.Response.Cookies.Append(
            CookieName,
            login.Token,
            CreateSessionCookieOptions(context.Request.IsHttps, login.ExpiresAtUtc));
    }

    internal static CookieOptions CreateSessionCookieOptions(bool isHttps, DateTime expiresAtUtc) => new()
    {
        HttpOnly = true,
        Secure = isHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = new DateTimeOffset(expiresAtUtc, TimeSpan.Zero),
        IsEssential = true
    };

    public static void ClearSessionCookie(HttpContext context) =>
        context.Response.Cookies.Delete(CookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/"
        });

    internal static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string? Limit(string? value, int maximum) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maximum ? value : value[..maximum];
}
