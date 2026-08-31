using Npgsql;

namespace RestaurantAgent.CentralApi;

internal sealed record AgentIdentity(Guid BranchId, Guid ConnectorInstallationId, string BranchCode);

/// <summary>
/// Autentica llamadas agente → API usando exclusivamente la identidad de dispositivo emitida al
/// vincular (ver ConnectorInstallationRegistry). No existe compatibilidad con el token
/// compartido legacy por sucursal: cada llamada debe traer <c>X-Connector-Id</c> +
/// <c>Authorization: Bearer</c> de un ConnectorInstallation activo.
/// </summary>
internal static class AgentAuthenticator
{
    public static async Task<AgentIdentity?> AuthenticateAsync(
        HttpContext context,
        NpgsqlDataSource dataSource,
        string branchCode,
        CancellationToken ct)
    {
        if (!context.Request.Headers.TryGetValue("X-Connector-Id", out var connectorHeader) ||
            !context.Request.Headers.TryGetValue("Authorization", out var authorization))
        {
            return null;
        }

        if (!Guid.TryParse(connectorHeader.ToString(), out var installationId) ||
            !authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        var token = authorization.ToString()["Bearer ".Length..].Trim();
        if (token.Length < 32) return null;

        await using var command = dataSource.CreateCommand("""
            UPDATE connector_installations c
            SET last_seen_at = now(),
                last_ip = $4,
                last_user_agent = $5,
                updated_at = now()
            FROM branches b
            WHERE c.id = $1
              AND c.token_hash = $2
              AND c.active = true
              AND c.branch_id = b.id
              AND b.code = $3
              AND b.active = true
            RETURNING c.branch_id;
            """);
        command.Parameters.AddWithValue(installationId);
        command.Parameters.AddWithValue(TokenHasher.Hash(token));
        command.Parameters.AddWithValue(branchCode);
        command.Parameters.AddWithValue(context.Connection.RemoteIpAddress?.ToString() ?? string.Empty);
        var userAgent = context.Request.Headers.UserAgent.ToString();
        command.Parameters.AddWithValue(userAgent[..Math.Min(userAgent.Length, 500)]);
        var result = await command.ExecuteScalarAsync(ct);
        return result is Guid branchId
            ? new AgentIdentity(branchId, installationId, branchCode)
            : null;
    }
}

/// <summary>
/// Quién autorizó una llamada a /api/admin/*. <see cref="User"/> es null cuando la
/// autorización vino de la llave estática (un script, sin cuenta asociada) — en ese caso no
/// hay "propia sesión" que proteger ni un id de actor para comparar contra el recurso que se
/// está modificando.
/// </summary>
internal sealed record AdminPrincipal(DashboardUser? User)
{
    public static readonly AdminPrincipal StaticKey = new((DashboardUser?)null);
    public static AdminPrincipal ForUser(DashboardUser user) => new(user);
}

internal static class AdminAuthenticator
{
    /// <summary>
    /// Autoriza los endpoints /api/admin/*: acepta la llave estática (scripts/automatización)
    /// o una sesión de panel con rol SUPERADMIN (dashboard-admin-web). Devuelve null si no
    /// autoriza; en caso contrario, indica quién hizo la llamada para que el endpoint pueda
    /// detectar auto-modificación (por ejemplo, un SUPERADMIN desactivando su propia cuenta).
    /// </summary>
    public static async Task<AdminPrincipal?> AuthorizeAsync(
        HttpContext context, ApiOptions options, WebAuthService auth, CancellationToken ct)
    {
        if (options.ConnectorAdminKey.Length >= 32 &&
            context.Request.Headers.TryGetValue("X-Admin-Key", out var supplied) &&
            TokenHasher.FixedTimeEquals(options.ConnectorAdminKey, supplied.ToString()))
        {
            return AdminPrincipal.StaticKey;
        }

        var user = await auth.AuthenticateAsync(context, ct);
        return user?.IsSuperAdmin == true ? AdminPrincipal.ForUser(user) : null;
    }

    /// <summary>Atajo para endpoints que solo necesitan saber sí/no (por ejemplo, sucursales).</summary>
    public static async Task<bool> IsAuthorizedAsync(
        HttpContext context, ApiOptions options, WebAuthService auth, CancellationToken ct) =>
        await AuthorizeAsync(context, options, auth, ct) is not null;
}
