using Npgsql;

namespace RestaurantAgent.CentralApi;

internal static class DbInitializer
{
    public static async Task InitializeAsync(NpgsqlDataSource dataSource, ApiOptions _, CancellationToken ct)
    {
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "schema.sql");
        var schema = await File.ReadAllTextAsync(schemaPath, ct);
        await using (var command = dataSource.CreateCommand(schema))
        {
            await command.ExecuteNonQueryAsync(ct);
        }
    }
}
