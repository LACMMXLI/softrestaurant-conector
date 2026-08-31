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

        if (string.IsNullOrWhiteSpace(options.BootstrapBranchCode)) return;

        // El negocio bootstrap usa el mismo slug 'negocio-principal' que el backfill de
        // schema.sql, para que una base ya migrada (con el negocio creado por el backfill) y una
        // base nueva (creada aquí) terminen en el mismo lugar sin duplicar negocios.
        Guid businessId;
        await using (var business = dataSource.CreateCommand("""
            INSERT INTO businesses (name, slug)
            VALUES ($1, 'negocio-principal')
            ON CONFLICT (slug) DO UPDATE SET updated_at = now()
            RETURNING id;
            """))
        {
            business.Parameters.AddWithValue(options.BootstrapBusinessName ?? "Negocio principal");
            businessId = (Guid)(await business.ExecuteScalarAsync(ct)
                ?? throw new InvalidOperationException("No se pudo crear el negocio bootstrap."));
        }

        await using var bootstrap = dataSource.CreateCommand("""
            INSERT INTO branches (business_id, code, name)
            VALUES ($1, $2, $3)
            ON CONFLICT (code) DO UPDATE
            SET business_id = excluded.business_id,
                name = excluded.name,
                active = true,
                updated_at = now();
            """);
        bootstrap.Parameters.AddWithValue(businessId);
        bootstrap.Parameters.AddWithValue(options.BootstrapBranchCode);
        bootstrap.Parameters.AddWithValue(options.BootstrapBranchName ?? options.BootstrapBranchCode);
        await bootstrap.ExecuteNonQueryAsync(ct);
    }
}
