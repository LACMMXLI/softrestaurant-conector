using Npgsql;

namespace SoftRestaurant.CentralApi;

internal static class DbInitializer
{
    public static async Task InitializeAsync(NpgsqlDataSource dataSource, ApiOptions options, CancellationToken ct)
    {
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "schema.sql");
        var schema = await File.ReadAllTextAsync(schemaPath, ct);
        await using (var command = dataSource.CreateCommand(schema))
        {
            await command.ExecuteNonQueryAsync(ct);
        }

        if (string.IsNullOrWhiteSpace(options.BootstrapBranchCode) ||
            string.IsNullOrWhiteSpace(options.LegacyBootstrapAgentToken))
        {
            return;
        }

        await using var bootstrap = dataSource.CreateCommand("""
            INSERT INTO branches (code, name, token_hash, legacy_auth_enabled)
            VALUES ($1, $2, $3, true)
            ON CONFLICT (code) DO UPDATE
            SET name = excluded.name,
                active = true,
                updated_at = now();
            """);
        bootstrap.Parameters.AddWithValue(options.BootstrapBranchCode);
        bootstrap.Parameters.AddWithValue(options.BootstrapBranchName ?? options.BootstrapBranchCode);
        bootstrap.Parameters.AddWithValue(TokenHasher.Hash(options.LegacyBootstrapAgentToken));
        await bootstrap.ExecuteNonQueryAsync(ct);
    }
}
