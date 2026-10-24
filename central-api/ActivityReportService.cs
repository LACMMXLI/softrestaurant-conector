using System.Text.Json;
using Npgsql;

namespace RestaurantAgent.CentralApi;

internal sealed record ActivityFilter(
    Guid? UserId,
    Guid? BusinessId,
    string? BranchCode,
    string? EventType,
    string? Action,
    DateTime? FromUtc,
    DateTime? ToUtc,
    bool IncludeAdmins);

internal sealed record ActivityItem(
    long Id,
    DateTime OccurredAt,
    Guid? UserId,
    string? UserEmail,
    string? UserName,
    string Source,
    string EventType,
    string Action,
    string? View,
    string? Method,
    string? Path,
    int? Status,
    int? DurationMs,
    Guid? BusinessId,
    string? BusinessName,
    string? BranchCode,
    string? BranchName,
    string? Ip,
    string? UserAgent,
    string? SessionRef,
    JsonElement Metadata);

internal sealed record ActivityPage(IReadOnlyList<ActivityItem> Items, int Page, int PageSize, bool HasMore);

internal sealed record ActivityTotals(long Events, long ActiveUsers, long Visits, long AppOpens, long Logins, long ActiveBusinesses);
internal sealed record ActivityDayPoint(DateOnly Day, long Events, long Users);
internal sealed record ActivityUserStat(Guid UserId, string? Email, string? Name, long Events, DateTime LastAt);
internal sealed record ActivityLabelStat(string Key, string? Label, long Events);
internal sealed record ActivitySummary(
    ActivityTotals Totals,
    IReadOnlyList<ActivityDayPoint> ByDay,
    IReadOnlyList<ActivityUserStat> TopUsers,
    IReadOnlyList<ActivityLabelStat> TopActions,
    IReadOnlyList<ActivityLabelStat> TopBusinesses);

/// <summary>Consultas del registro de actividad para el panel de operador (solo lectura).</summary>
internal sealed class ActivityReportService(NpgsqlDataSource dataSource)
{
    public const int MaxPageSize = 100;

    // Mismo predicado para el listado y el resumen. El rol SUPERADMIN se excluye por defecto
    // para que el uso del propio operador no se mezcle con el de los clientes.
    private const string FilterSql = """
        FROM activity_log a
        LEFT JOIN app_users u ON u.id = a.user_id
        WHERE ($1::uuid IS NULL OR a.user_id = $1::uuid)
          AND ($2::uuid IS NULL OR a.business_id = $2::uuid)
          AND ($3::text IS NULL OR a.branch_code = $3::text)
          AND ($4::text IS NULL OR a.event_type = $4::text)
          AND ($5::text IS NULL OR a.action = $5::text)
          AND ($6::timestamptz IS NULL OR a.occurred_at >= $6::timestamptz)
          AND ($7::timestamptz IS NULL OR a.occurred_at < $7::timestamptz)
          AND ($8::boolean OR u.role IS DISTINCT FROM 'SUPERADMIN')
        """;

    public async Task<ActivityPage> ListAsync(ActivityFilter filter, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        await using var command = dataSource.CreateCommand($"""
            SELECT a.id, a.occurred_at, a.user_id, COALESCE(a.user_email, u.email), COALESCE(a.user_name, u.display_name),
                   a.source, a.event_type, a.action, a.view, a.method, a.path, a.status, a.duration_ms,
                   a.business_id, a.business_name, a.branch_code, a.branch_name, a.ip, a.user_agent, a.session_ref,
                   a.metadata::text
            {FilterSql}
            ORDER BY a.occurred_at DESC, a.id DESC
            OFFSET $9 LIMIT $10;
            """);
        AddFilter(command, filter);
        command.Parameters.AddWithValue((page - 1) * pageSize);
        command.Parameters.AddWithValue(pageSize + 1);

        var items = new List<ActivityItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            using var metadata = JsonDocument.Parse(reader.GetString(20));
            items.Add(new ActivityItem(
                reader.GetInt64(0), reader.GetDateTime(1), Guid(reader, 2), Text(reader, 3), Text(reader, 4),
                reader.GetString(5), reader.GetString(6), reader.GetString(7), Text(reader, 8), Text(reader, 9),
                Text(reader, 10), Int(reader, 11), Int(reader, 12), Guid(reader, 13), Text(reader, 14),
                Text(reader, 15), Text(reader, 16), Text(reader, 17), Text(reader, 18), Text(reader, 19),
                metadata.RootElement.Clone()));
        }

        var hasMore = items.Count > pageSize;
        return new ActivityPage(hasMore ? items.Take(pageSize).ToList() : items, page, pageSize, hasMore);
    }

    public async Task<ActivitySummary> GetSummaryAsync(ActivityFilter filter, string? timeZone, CancellationToken ct)
    {
        var totals = await QueryTotalsAsync(filter, ct);
        var days = new List<ActivityDayPoint>();
        await using (var command = dataSource.CreateCommand($"""
            SELECT (a.occurred_at AT TIME ZONE COALESCE((SELECT name FROM pg_timezone_names WHERE name = $9::text), 'UTC'))::date AS day,
                   COUNT(*), COUNT(DISTINCT a.user_id)
            {FilterSql}
            GROUP BY day ORDER BY day;
            """))
        {
            AddFilter(command, filter);
            command.Parameters.AddWithValue((object?)timeZone ?? DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                days.Add(new ActivityDayPoint(DateOnly.FromDateTime(reader.GetDateTime(0)), reader.GetInt64(1), reader.GetInt64(2)));
        }

        var users = new List<ActivityUserStat>();
        await using (var command = dataSource.CreateCommand($"""
            SELECT a.user_id, MAX(COALESCE(a.user_email, u.email)), MAX(COALESCE(a.user_name, u.display_name)),
                   COUNT(*), MAX(a.occurred_at)
            {FilterSql} AND a.user_id IS NOT NULL
            GROUP BY a.user_id ORDER BY COUNT(*) DESC, MAX(a.occurred_at) DESC LIMIT 10;
            """))
        {
            AddFilter(command, filter);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                users.Add(new ActivityUserStat(reader.GetGuid(0), Text(reader, 1), Text(reader, 2), reader.GetInt64(3), reader.GetDateTime(4)));
        }

        var actions = await QueryLabelStatsAsync(filter, "a.action", "NULL::text", "a.action", ct);
        var businesses = await QueryLabelStatsAsync(filter, "a.business_id::text", "MAX(a.business_name)", "a.business_id::text", ct, "AND a.business_id IS NOT NULL");
        return new ActivitySummary(totals, days, users, actions, businesses);
    }

    private async Task<ActivityTotals> QueryTotalsAsync(ActivityFilter filter, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand($"""
            SELECT COUNT(*), COUNT(DISTINCT a.user_id), COUNT(DISTINCT a.session_ref),
                   COUNT(*) FILTER (WHERE a.action = 'app_open'),
                   COUNT(*) FILTER (WHERE a.action = 'auth.login'),
                   COUNT(DISTINCT a.business_id)
            {FilterSql};
            """);
        AddFilter(command, filter);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new ActivityTotals(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5));
    }

    private async Task<IReadOnlyList<ActivityLabelStat>> QueryLabelStatsAsync(
        ActivityFilter filter, string keyExpression, string labelExpression, string groupBy, CancellationToken ct, string extraWhere = "")
    {
        var result = new List<ActivityLabelStat>();
        await using var command = dataSource.CreateCommand($"""
            SELECT {keyExpression}, {labelExpression}, COUNT(*)
            {FilterSql} {extraWhere}
            GROUP BY {groupBy} ORDER BY COUNT(*) DESC, {groupBy} LIMIT 12;
            """);
        AddFilter(command, filter);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(new ActivityLabelStat(reader.GetString(0), Text(reader, 1), reader.GetInt64(2)));
        return result;
    }

    private static void AddFilter(NpgsqlCommand command, ActivityFilter f)
    {
        command.Parameters.AddWithValue((object?)f.UserId ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)f.BusinessId ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)f.BranchCode ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)f.EventType ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)f.Action ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)f.FromUtc ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)f.ToUtc ?? DBNull.Value);
        command.Parameters.AddWithValue(f.IncludeAdmins);
    }

    private static string? Text(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static int? Int(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    private static Guid? Guid(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
}
