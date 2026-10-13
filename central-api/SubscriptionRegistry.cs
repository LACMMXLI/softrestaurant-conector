using Npgsql;

namespace RestaurantAgent.CentralApi;

internal sealed record SubscriptionView(
    string Plan,
    string Status,
    DateTime TrialEndsAt,
    DateTime? PaidUntil,
    bool Suspended,
    bool CanAccessContent,
    int TrialDaysRemaining,
    int? HistoryDays);

internal sealed record SubscriptionActivationRequest(string Plan, int Months);
internal sealed record SubscriptionStatusRequest(bool Suspended);

internal static class SubscriptionPolicy
{
    public const int BasicBranchLimit = 1;
    public const int PlusBranchLimit = 5;

    public static bool IsValidPlan(string? plan) => plan is "BASIC" or "PLUS" or "UNLIMITED";
    public static bool IsValidDuration(int months) => months is 1 or 2 or 3 or 6;
    // El nivel de plan ya no recorta la antigüedad de los hechos conservados. Así, al
    // reactivar el acceso se puede consultar también lo recibido mientras el cliente estaba
    // suspendido o vencido. La disponibilidad depende de lo que se haya sincronizado.
    public static int? GetHistoryDays(string plan) => null;
    public static int GetBranchLimit(string plan) => plan == "BASIC" ? BasicBranchLimit : PlusBranchLimit;
    public static bool CanUseConsolidatedDashboard(string plan) => plan is "PLUS" or "UNLIMITED";
    public static DateOnly? GetOldestAvailableDate(string plan, DateOnly today) =>
        GetHistoryDays(plan) is { } days ? today.AddDays(-(days - 1)) : null;

    // La suscripción controla las superficies interactivas del cliente. La identidad del
    // conector autentica el agente por separado; no se debe aplicar este bloqueo a ingesta,
    // heartbeat ni endpoints operativos del dispositivo.
    public static bool RequiresActiveSubscription(PathString path) =>
        path.StartsWithSegments("/api/web") &&
        !path.StartsWithSegments("/api/web/auth") &&
        !path.StartsWithSegments("/api/web/agent/latest");

    public static SubscriptionView Evaluate(
        string plan, DateTime trialEndsAt, DateTime? paidUntil, bool suspended, DateTime utcNow)
    {
        var trialActive = trialEndsAt > utcNow;
        var paidActive = paidUntil is not null && paidUntil > utcNow;
        var canAccess = !suspended && (trialActive || paidActive);
        var status = suspended ? "SUSPENDED" : paidActive ? "ACTIVE" : trialActive ? "TRIAL" : "EXPIRED";
        var remaining = trialActive ? Math.Max(1, (int)Math.Ceiling((trialEndsAt - utcNow).TotalDays)) : 0;
        return new SubscriptionView(plan, status, trialEndsAt, paidUntil, suspended, canAccess, remaining,
            GetHistoryDays(plan));
    }
}

internal sealed class SubscriptionRegistry(NpgsqlDataSource dataSource)
{
    public async Task<SubscriptionView?> GetAsync(Guid userId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT subscription_plan, trial_ends_at, paid_until, subscription_suspended
            FROM app_users WHERE id = $1;
            """);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return SubscriptionPolicy.Evaluate(
            reader.GetString(0), reader.GetDateTime(1), reader.IsDBNull(2) ? null : reader.GetDateTime(2),
            reader.GetBoolean(3), DateTime.UtcNow);
    }

    public async Task<SubscriptionView?> ActivateAsync(Guid userId, string plan, int months, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE app_users
            SET subscription_plan = $2,
                paid_until = GREATEST(COALESCE(paid_until, now()), now()) + make_interval(months => $3),
                subscription_suspended = false,
                subscription_updated_at = now(),
                updated_at = now()
            WHERE id = $1
            RETURNING subscription_plan, trial_ends_at, paid_until, subscription_suspended;
            """);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(plan);
        command.Parameters.AddWithValue(months);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return SubscriptionPolicy.Evaluate(
            reader.GetString(0), reader.GetDateTime(1), reader.GetDateTime(2), reader.GetBoolean(3), DateTime.UtcNow);
    }

    public async Task<SubscriptionView?> SetSuspendedAsync(Guid userId, bool suspended, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE app_users SET subscription_suspended = $2, subscription_updated_at = now(), updated_at = now()
            WHERE id = $1
            RETURNING subscription_plan, trial_ends_at, paid_until, subscription_suspended;
            """);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(suspended);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return SubscriptionPolicy.Evaluate(
            reader.GetString(0), reader.GetDateTime(1), reader.IsDBNull(2) ? null : reader.GetDateTime(2),
            reader.GetBoolean(3), DateTime.UtcNow);
    }

    public async Task<SubscriptionView?> ResetPaidTimeAsync(Guid userId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE app_users SET paid_until = NULL, subscription_updated_at = now(), updated_at = now()
            WHERE id = $1
            RETURNING subscription_plan, trial_ends_at, paid_until, subscription_suspended;
            """);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return SubscriptionPolicy.Evaluate(
            reader.GetString(0), reader.GetDateTime(1), reader.IsDBNull(2) ? null : reader.GetDateTime(2),
            reader.GetBoolean(3), DateTime.UtcNow);
    }
}
