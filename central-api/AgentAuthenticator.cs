using Npgsql;

namespace SoftRestaurant.CentralApi;

internal sealed record AgentIdentity(Guid BranchId, Guid? ConnectorId, string BranchCode, bool Legacy);

internal static class AgentAuthenticator
{
    public static async Task<AgentIdentity?> AuthenticateAsync(
        HttpContext context,
        NpgsqlDataSource dataSource,
        string branchCode,
        CancellationToken ct)
    {
        var hasConnectorId = context.Request.Headers.TryGetValue("X-Connector-Id", out var connectorHeader);
        var hasAuthorization = context.Request.Headers.TryGetValue("Authorization", out var authorization);
        if (hasConnectorId || hasAuthorization)
        {
            if (!Guid.TryParse(connectorHeader.ToString(), out var connectorId) ||
                !authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return null;

            var token = authorization.ToString()["Bearer ".Length..].Trim();
            if (token.Length < 32) return null;

            await using var command = dataSource.CreateCommand("""
                UPDATE connectors c
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
            command.Parameters.AddWithValue(connectorId);
            command.Parameters.AddWithValue(TokenHasher.Hash(token));
            command.Parameters.AddWithValue(branchCode);
            command.Parameters.AddWithValue(context.Connection.RemoteIpAddress?.ToString() ?? string.Empty);
            var userAgent = context.Request.Headers.UserAgent.ToString();
            command.Parameters.AddWithValue(userAgent[..Math.Min(userAgent.Length, 500)]);
            var result = await command.ExecuteScalarAsync(ct);
            return result is Guid branchId
                ? new AgentIdentity(branchId, connectorId, branchCode, Legacy: false)
                : null;
        }

        // LEGACY: token compartido por sucursal. Retirar después de migrar todas las instalaciones.
        if (!context.Request.Headers.TryGetValue("X-Agent-Token", out var legacyHeader) ||
            string.IsNullOrWhiteSpace(legacyHeader))
            return null;

        await using var legacyCommand = dataSource.CreateCommand("""
            SELECT id
            FROM branches
            WHERE code = $1
              AND token_hash = $2
              AND active = true
              AND legacy_auth_enabled = true;
            """);
        legacyCommand.Parameters.AddWithValue(branchCode);
        legacyCommand.Parameters.AddWithValue(TokenHasher.Hash(legacyHeader.ToString()));
        var legacyResult = await legacyCommand.ExecuteScalarAsync(ct);
        if (legacyResult is not Guid legacyBranchId) return null;

        context.Response.Headers["X-Agent-Auth-Mode"] = "legacy";
        return new AgentIdentity(legacyBranchId, null, branchCode, Legacy: true);
    }
}

internal static class AdminAuthenticator
{
    public static bool IsAuthorized(HttpContext context, ApiOptions options) =>
        options.ConnectorAdminKey.Length >= 32 &&
        context.Request.Headers.TryGetValue("X-Admin-Key", out var supplied) &&
        TokenHasher.FixedTimeEquals(options.ConnectorAdminKey, supplied.ToString());
}
