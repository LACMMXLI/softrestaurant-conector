using Npgsql;

namespace RestaurantAgent.CentralApi;

internal sealed record SubscriptionNoticeDesignRequest(string Design);

internal sealed class PlatformSettingsRegistry(NpgsqlDataSource dataSource)
{
    public const string DefaultSubscriptionNoticeDesign = "A";

    public async Task<string> GetSubscriptionNoticeDesignAsync(CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT value FROM platform_settings WHERE key = 'subscription_notice_design';
            """);
        return await command.ExecuteScalarAsync(ct) as string ?? DefaultSubscriptionNoticeDesign;
    }

    public async Task SetSubscriptionNoticeDesignAsync(string design, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            INSERT INTO platform_settings (key, value, updated_at)
            VALUES ('subscription_notice_design', $1, now())
            ON CONFLICT (key) DO UPDATE SET value = excluded.value, updated_at = now();
            """);
        command.Parameters.AddWithValue(design);
        await command.ExecuteNonQueryAsync(ct);
    }
}
