using Npgsql;

namespace RestaurantAgent.CentralApi;

internal sealed record DashboardBranch(
    string Code,
    string Name,
    string Timezone,
    DateTime? LastSyncAt,
    string Freshness,
    bool? ReconciliationOk,
    DateTime? RangeStart,
    DateTime? RangeEnd,
    DateTime? SyncRequestedAt);

internal sealed record DashboardMeta(
    Guid BranchId,
    string BranchCode,
    string BranchName,
    string Timezone,
    DateOnly Date,
    DateTime? LastSyncAt,
    string? LastBatchId,
    DateTime? RangeStart,
    DateTime? RangeEnd,
    bool? ReconciliationOk,
    string Freshness,
    string Coverage,
    bool CanShowData,
    int? ShiftId,
    int? ShiftNumber);

internal sealed record DashboardShift(
    int Id,
    int Number,
    DateTime? OpenedAt,
    DateTime? ClosedAt,
    string? Cashier,
    bool IsOpen);

internal sealed record DashboardSummary(
    long? Tickets,
    decimal? Sales,
    decimal? AverageTicket,
    decimal? Tips,
    long? CancelledTickets,
    long? CancelledLines,
    decimal? CashIn,
    decimal? CashOut,
    decimal? CashSales,
    decimal? CardSales,
    decimal? OtherSales,
    decimal? OpeningFund,
    decimal? DeclaredCash,
    decimal? ExpectedCash,
    decimal? CashDifference,
    bool PaymentBreakdownComplete,
    decimal? PreviousSales,
    decimal? SalesChangePercent);

internal sealed record HourlySalesPoint(int Hour, decimal Sales, long Tickets);

internal sealed record SalesTicketItem(
    long Folio,
    string? CheckNumber,
    DateTime? OpenedAt,
    DateTime? ClosedAt,
    decimal? Total,
    decimal? Tip,
    bool Paid,
    bool Cancelled,
    string? Table,
    string? PaymentUser);

internal sealed record TicketLineItem(
    string? ProductId,
    string? ProductName,
    decimal? Quantity,
    decimal? Price,
    decimal? Discount,
    string? Comment);

internal sealed record TicketPaymentItem(
    string? PaymentMethodId,
    string? PaymentMethodName,
    int? PaymentMethodType,
    decimal? Amount,
    decimal? Tip,
    decimal? ExchangeRate,
    string? CardBrand);

internal sealed record TicketDetail(
    SalesTicketItem Ticket,
    string? Station,
    string? RestaurantArea,
    string? WaiterId,
    string? CancellationReason,
    string? CancelledBy,
    IReadOnlyList<TicketLineItem> Lines,
    IReadOnlyList<TicketPaymentItem> Payments);

internal sealed record CancellationItem(
    DateTime Date,
    long? Folio,
    string? ProductId,
    string? Description,
    decimal? Quantity,
    decimal? Price,
    int Occurrences,
    string? User,
    string? Reason);

internal sealed record CashMovementItem(
    long Folio,
    DateTime? Date,
    int Type,
    decimal? Amount,
    string? Concept,
    string? Reference);

internal sealed record DashboardHomeResponse(
    DashboardMeta Meta,
    DashboardSummary Summary,
    IReadOnlyList<HourlySalesPoint> HourlySales,
    IReadOnlyList<SalesTicketItem> RecentTickets,
    IReadOnlyList<CancellationItem> RecentCancellations,
    IReadOnlyList<CashMovementItem> RecentCashMovements);

internal sealed record SalesPage(
    DashboardMeta Meta,
    IReadOnlyList<SalesTicketItem> Items,
    int Page,
    int PageSize,
    bool HasMore);

internal sealed record CashMovementsPage(
    DashboardMeta Meta,
    IReadOnlyList<CashMovementItem> Items,
    int Page,
    int PageSize,
    bool HasMore);

internal sealed class DashboardReportService(NpgsqlDataSource dataSource, ApiOptions options)
{
    public async Task<IReadOnlyList<DashboardBranch>> GetBranchesAsync(
        DashboardUser user,
        CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT b.code, b.name, b.timezone, b.last_sync_at,
                   sb.reconciliation_ok, sb.range_start, sb.range_end, b.sync_requested_at
            FROM branches b
            LEFT JOIN LATERAL (
                SELECT reconciliation_ok, range_start, range_end
                FROM sync_batches
                WHERE branch_id = b.id
                ORDER BY received_at DESC
                LIMIT 1
            ) sb ON true
            WHERE b.active = true
              -- Regla de acceso (ver BusinessAccess.CanAccessBusiness / BusinessAccessTests):
              -- /api/web/* nunca usa el atajo de SUPERADMIN — incluso un operador de plataforma
              -- solo ve las sucursales de negocios de los que es miembro explícito ahí. El
              -- acceso incondicional queda reservado a /api/admin/* (AdminAuthenticator).
              AND EXISTS (
                  SELECT 1 FROM business_members bm
                  WHERE bm.user_id = $1 AND bm.business_id = b.business_id
              )
            ORDER BY b.name, b.code;
            """);
        command.Parameters.AddWithValue(user.Id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var branches = new List<DashboardBranch>();
        while (await reader.ReadAsync(ct))
        {
            var lastSyncAt = ReadNullableDateTime(reader, 3);
            branches.Add(new DashboardBranch(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                lastSyncAt,
                GetFreshness(lastSyncAt),
                ReadNullableBool(reader, 4),
                ReadNullableDateTime(reader, 5),
                ReadNullableDateTime(reader, 6),
                ReadNullableDateTime(reader, 7)));
        }
        return branches;
    }

    public async Task<IReadOnlyList<DashboardShift>> GetShiftsAsync(
        DashboardUser user, string branchCode, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT s.source_shift_id, NULLIF(s.payload->>'idTurno', '')::integer,
                   s.opened_at, s.closed_at, s.payload->>'cajero', s.closed_at IS NULL
            FROM shifts s
            INNER JOIN branches b ON b.id = s.branch_id
            WHERE b.active = true AND b.code = $1
              AND EXISTS (SELECT 1 FROM business_members bm
                          WHERE bm.user_id = $2 AND bm.business_id = b.business_id)
            ORDER BY (s.closed_at IS NULL) DESC, s.opened_at DESC NULLS LAST, s.source_shift_id DESC;
            """);
        command.Parameters.AddWithValue(branchCode);
        command.Parameters.AddWithValue(user.Id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<DashboardShift>();
        while (await reader.ReadAsync(ct))
            result.Add(new DashboardShift(reader.GetInt32(0), reader.GetInt32(1), ReadNullableDateTime(reader, 2),
                ReadNullableDateTime(reader, 3), ReadNullableString(reader, 4), reader.GetBoolean(5)));
        return result;
    }

    /// <summary>
    /// Marca una solicitud de sincronización remota para la sucursal (mismo mecanismo simple
    /// que usa admin-web: un timestamp en <c>branches.sync_requested_at</c>, sin cola de
    /// comandos). Aplica la misma regla de acceso que <see cref="GetBranchesAsync"/>
    /// (ver <c>BranchAccess.CanAccessBranch</c>); el llamador ya debe haber verificado que el
    /// rol del usuario puede escribir (no VIEWER).
    /// </summary>
    public async Task<DateTime?> RequestSyncAsync(DashboardUser user, string branchCode, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE branches b
            SET sync_requested_at = now(), sync_requested_by = $3, updated_at = now()
            WHERE b.active = true AND b.code = $1
              -- Misma regla que GetBranchesAsync: ver BusinessAccess.CanAccessBusiness.
              AND EXISTS (
                  SELECT 1 FROM business_members bm
                  WHERE bm.user_id = $2 AND bm.business_id = b.business_id
              )
            RETURNING sync_requested_at;
            """);
        command.Parameters.AddWithValue(branchCode);
        command.Parameters.AddWithValue(user.Id);
        command.Parameters.AddWithValue(user.Id);
        return await command.ExecuteScalarAsync(ct) as DateTime?;
    }

    public async Task<DashboardHomeResponse?> GetHomeAsync(
        DashboardUser user,
        string branchCode,
        DateOnly date,
        int? shiftId,
        CancellationToken ct)
    {
        var meta = await GetMetaAsync(user, branchCode, date, shiftId, ct);
        if (meta is null) return null;
        if (!meta.CanShowData)
        {
            return new DashboardHomeResponse(
                meta,
                EmptySummary,
                [],
                [],
                [],
                []);
        }

        var summaryTask = GetSummaryAsync(meta, ct);
        var hourlyTask = GetHourlySalesAsync(meta, ct);
        var ticketsTask = GetTicketItemsAsync(meta, page: 1, pageSize: 6, search: null, ct);
        var cancellationsTask = GetCancellationsAsync(meta, limit: 5, ct);
        var cashTask = GetCashMovementItemsAsync(meta, page: 1, pageSize: 5, type: null, search: null, ct);
        await Task.WhenAll(summaryTask, hourlyTask, ticketsTask, cancellationsTask, cashTask);

        return new DashboardHomeResponse(
            meta,
            await summaryTask,
            await hourlyTask,
            (await ticketsTask).Items,
            await cancellationsTask,
            (await cashTask).Items);
    }

    public async Task<SalesPage?> GetSalesAsync(
        DashboardUser user,
        string branchCode,
        DateOnly date,
        int? shiftId,
        int page,
        int pageSize,
        string? search,
        CancellationToken ct)
    {
        var meta = await GetMetaAsync(user, branchCode, date, shiftId, ct);
        if (meta is null) return null;
        if (!meta.CanShowData) return new SalesPage(meta, [], page, pageSize, false);
        return await GetTicketItemsAsync(meta, page, pageSize, search, ct);
    }

    public async Task<CashMovementsPage?> GetCashMovementsPageAsync(
        DashboardUser user,
        string branchCode,
        DateOnly date,
        int? shiftId,
        int page,
        int pageSize,
        int? type,
        string? search,
        CancellationToken ct)
    {
        var meta = await GetMetaAsync(user, branchCode, date, shiftId, ct);
        if (meta is null) return null;
        if (!meta.CanShowData) return new CashMovementsPage(meta, [], page, pageSize, false);
        return await GetCashMovementItemsAsync(meta, page, pageSize, type, search, ct);
    }

    public async Task<TicketDetail?> GetTicketAsync(
        DashboardUser user,
        string branchCode,
        long folio,
        CancellationToken ct)
    {
        var branch = await GetBranchIdentityAsync(user, branchCode, ct);
        if (branch is null) return null;

        SalesTicketItem? ticket = null;
        string? station = null;
        string? restaurantArea = null;
        string? waiterId = null;
        string? cancellationReason = null;
        string? cancelledBy = null;

        await using (var command = dataSource.CreateCommand("""
            SELECT source_folio,
                   payload->>'numCheque', business_date, closed_at, total, tip, paid, cancelled,
                   payload->>'mesa', payload->>'usuarioPago', payload->>'estacion',
                   payload->>'idAreaRestaurant', payload->>'idMesero',
                   payload->>'razonCancelado', payload->>'usuarioCancelo'
            FROM sales
            WHERE branch_id = $1 AND source_folio = $2
            ORDER BY updated_at DESC
            LIMIT 1;
            """))
        {
            command.Parameters.AddWithValue(branch.Value.Id);
            command.Parameters.AddWithValue(folio);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            ticket = ReadTicket(reader);
            station = ReadNullableString(reader, 10);
            restaurantArea = ReadNullableString(reader, 11);
            waiterId = ReadNullableString(reader, 12);
            cancellationReason = ReadNullableString(reader, 13);
            cancelledBy = ReadNullableString(reader, 14);
        }

        var lines = new List<TicketLineItem>();
        await using (var command = dataSource.CreateCommand("""
            SELECT product_id, NULLIF(payload->>'descripcionProducto', ''), quantity, price,
                   NULLIF(payload->>'descuento', '')::numeric,
                   payload->>'comentario'
            FROM sale_lines
            WHERE branch_id = $1 AND source_folio = $2
            ORDER BY NULLIF(payload->>'hora', '')::timestamp NULLS LAST, idempotency_key;
            """))
        {
            command.Parameters.AddWithValue(branch.Value.Id);
            command.Parameters.AddWithValue(folio);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                lines.Add(new TicketLineItem(
                    ReadNullableString(reader, 0),
                    ReadNullableString(reader, 1),
                    ReadNullableDecimal(reader, 2),
                    ReadNullableDecimal(reader, 3),
                    ReadNullableDecimal(reader, 4),
                    ReadNullableString(reader, 5)));
            }
        }

        var payments = new List<TicketPaymentItem>();
        await using (var command = dataSource.CreateCommand("""
            SELECT payment_method, NULLIF(payload->>'descripcionFormaDePago', ''),
                   NULLIF(payload->>'tipoFormaDePago', '')::integer,
                   amount, tip, exchange_rate, payload->>'cardBrand'
            FROM sale_payments
            WHERE branch_id = $1 AND source_folio = $2
            ORDER BY idempotency_key;
            """))
        {
            command.Parameters.AddWithValue(branch.Value.Id);
            command.Parameters.AddWithValue(folio);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                payments.Add(new TicketPaymentItem(
                    ReadNullableString(reader, 0),
                    ReadNullableString(reader, 1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    ReadNullableDecimal(reader, 3),
                    ReadNullableDecimal(reader, 4),
                    ReadNullableDecimal(reader, 5),
                    ReadNullableString(reader, 6)));
            }
        }

        return new TicketDetail(
            ticket,
            station,
            restaurantArea,
            waiterId,
            cancellationReason,
            cancelledBy,
            lines,
            payments);
    }

    private async Task<DashboardMeta?> GetMetaAsync(
        DashboardUser user,
        string branchCode,
        DateOnly date,
        int? shiftId,
        CancellationToken ct)
    {
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        await using var command = dataSource.CreateCommand("""
            SELECT b.id, b.code, b.name, b.timezone, b.last_sync_at,
                   sb.id, sb.range_start, sb.range_end, sb.reconciliation_ok,
                   (SELECT NULLIF(s.payload->>'idTurno', '')::integer
                    FROM shifts s WHERE s.branch_id = b.id AND s.source_shift_id = $5 LIMIT 1)
            FROM branches b
            LEFT JOIN LATERAL (
                SELECT id, range_start, range_end, reconciliation_ok
                FROM sync_batches
                WHERE branch_id = b.id
                  AND range_start < $4
                  AND range_end > $3
                ORDER BY received_at DESC
                LIMIT 1
            ) sb ON true
            WHERE b.active = true
              AND b.code = $1
              -- Misma regla que GetBranchesAsync: ver BusinessAccess.CanAccessBusiness.
              AND EXISTS (
                  SELECT 1 FROM business_members bm
                  WHERE bm.user_id = $2 AND bm.business_id = b.business_id
              );
            """);
        command.Parameters.AddWithValue(branchCode);
        command.Parameters.AddWithValue(user.Id);
        command.Parameters.AddWithValue(start);
        command.Parameters.AddWithValue(end);
        command.Parameters.AddWithValue((object?)shiftId ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var lastSyncAt = ReadNullableDateTime(reader, 4);
        var batchId = ReadNullableString(reader, 5);
        var rangeStart = ReadNullableDateTime(reader, 6);
        var rangeEnd = ReadNullableDateTime(reader, 7);
        var reconciliationOk = ReadNullableBool(reader, 8);
        var coverage = GetCoverage(date, batchId, rangeStart, rangeEnd, reconciliationOk);
        return new DashboardMeta(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            date,
            lastSyncAt,
            batchId,
            rangeStart,
            rangeEnd,
            reconciliationOk,
            GetFreshness(lastSyncAt),
            coverage,
            reconciliationOk == true && coverage is "complete" or "partial",
            shiftId,
            reader.IsDBNull(9) ? null : reader.GetInt32(9));
    }

    private async Task<(Guid Id, string Timezone)?> GetBranchIdentityAsync(
        DashboardUser user,
        string branchCode,
        CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT b.id, b.timezone
            FROM branches b
            WHERE b.active = true AND b.code = $1
              -- Misma regla que GetBranchesAsync: ver BusinessAccess.CanAccessBusiness.
              AND EXISTS (
                  SELECT 1 FROM business_members bm
                  WHERE bm.user_id = $2 AND bm.business_id = b.business_id
              );
            """);
        command.Parameters.AddWithValue(branchCode);
        command.Parameters.AddWithValue(user.Id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? (reader.GetGuid(0), reader.GetString(1))
            : null;
    }

    private async Task<DashboardSummary> GetSummaryAsync(DashboardMeta meta, CancellationToken ct)
    {
        var start = meta.Date.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        var previousStart = start.AddDays(-1);
        await using var command = dataSource.CreateCommand("""
            SELECT
                COUNT(*) FILTER (
                    WHERE business_date >= $2 AND business_date < $3
                      AND paid AND NOT cancelled AND closed_at IS NOT NULL),
                COALESCE(SUM(total) FILTER (
                    WHERE business_date >= $2 AND business_date < $3
                      AND paid AND NOT cancelled AND closed_at IS NOT NULL), 0),
                COALESCE(AVG(total) FILTER (
                    WHERE business_date >= $2 AND business_date < $3
                      AND paid AND NOT cancelled AND closed_at IS NOT NULL), 0),
                COALESCE(SUM(tip) FILTER (
                    WHERE business_date >= $2 AND business_date < $3
                      AND paid AND NOT cancelled AND closed_at IS NOT NULL), 0),
                COUNT(*) FILTER (
                    WHERE business_date >= $2 AND business_date < $3 AND cancelled),
                COALESCE(SUM(total) FILTER (
                    WHERE business_date >= $4 AND business_date < $2
                      AND paid AND NOT cancelled AND closed_at IS NOT NULL), 0),
                COALESCE((SELECT SUM(occurrences) FROM cancellation_summaries
                    WHERE branch_id = $1 AND cancellation_date = $2::date), 0),
                COALESCE((SELECT SUM(amount) FROM cash_movements
                    WHERE branch_id = $1 AND movement_date >= $2 AND movement_date < $3
                      AND ($5::integer IS NULL OR COALESCE(source_shift_id, NULLIF(payload->>'idTurno', '')::integer) = $5)
                      AND movement_type = 2 AND NOT cancelled), 0),
                COALESCE((SELECT SUM(amount) FROM cash_movements
                    WHERE branch_id = $1 AND movement_date >= $2 AND movement_date < $3
                      AND ($5::integer IS NULL OR COALESCE(source_shift_id, NULLIF(payload->>'idTurno', '')::integer) = $5)
                      AND movement_type = 1 AND NOT cancelled), 0),
                COALESCE((SELECT SUM(sp.amount * COALESCE(NULLIF(sp.exchange_rate, 0), 1))
                    FROM sale_payments sp
                    INNER JOIN sales paid_sale
                      ON paid_sale.branch_id = sp.branch_id
                     AND paid_sale.source_folio = sp.source_folio
                    WHERE sp.branch_id = $1
                      AND paid_sale.business_date >= $2 AND paid_sale.business_date < $3
                      AND ($5::integer IS NULL OR COALESCE(paid_sale.source_shift_id, NULLIF(paid_sale.payload->>'idTurno', '')::integer) = $5)
                      AND paid_sale.paid AND NOT paid_sale.cancelled AND paid_sale.closed_at IS NOT NULL
                      AND NULLIF(sp.payload->>'tipoFormaDePago', '')::integer = 1), 0),
                COALESCE((SELECT SUM(sp.amount * COALESCE(NULLIF(sp.exchange_rate, 0), 1))
                    FROM sale_payments sp
                    INNER JOIN sales paid_sale
                      ON paid_sale.branch_id = sp.branch_id
                     AND paid_sale.source_folio = sp.source_folio
                    WHERE sp.branch_id = $1
                      AND paid_sale.business_date >= $2 AND paid_sale.business_date < $3
                      AND ($5::integer IS NULL OR COALESCE(paid_sale.source_shift_id, NULLIF(paid_sale.payload->>'idTurno', '')::integer) = $5)
                      AND paid_sale.paid AND NOT paid_sale.cancelled AND paid_sale.closed_at IS NOT NULL
                      AND NULLIF(sp.payload->>'tipoFormaDePago', '')::integer = 2), 0),
                COALESCE((SELECT SUM(sp.amount * COALESCE(NULLIF(sp.exchange_rate, 0), 1))
                    FROM sale_payments sp
                    INNER JOIN sales paid_sale
                      ON paid_sale.branch_id = sp.branch_id
                     AND paid_sale.source_folio = sp.source_folio
                    WHERE sp.branch_id = $1
                      AND paid_sale.business_date >= $2 AND paid_sale.business_date < $3
                      AND ($5::integer IS NULL OR COALESCE(paid_sale.source_shift_id, NULLIF(paid_sale.payload->>'idTurno', '')::integer) = $5)
                      AND paid_sale.paid AND NOT paid_sale.cancelled AND paid_sale.closed_at IS NOT NULL
                      AND NULLIF(sp.payload->>'tipoFormaDePago', '')::integer IN (3, 4)), 0),
                COALESCE((SELECT SUM(NULLIF(payload->>'fondo', '')::numeric)
                    FROM shifts
                    WHERE branch_id = $1 AND opened_at >= $2 AND opened_at < $3
                      AND ($5::integer IS NULL OR NULLIF(payload->>'idTurno', '')::integer = $5)), 0),
                COALESCE((SELECT SUM(NULLIF(payload->>'efectivo', '')::numeric)
                    FROM shifts
                    WHERE branch_id = $1 AND opened_at >= $2 AND opened_at < $3
                      AND ($5::integer IS NULL OR NULLIF(payload->>'idTurno', '')::integer = $5)), 0),
                COALESCE((SELECT bool_and(NULLIF(sp.payload->>'tipoFormaDePago', '') IS NOT NULL)
                    FROM sale_payments sp
                    INNER JOIN sales paid_sale
                      ON paid_sale.branch_id = sp.branch_id
                     AND paid_sale.source_folio = sp.source_folio
                    WHERE sp.branch_id = $1
                      AND paid_sale.business_date >= $2 AND paid_sale.business_date < $3
                      AND ($5::integer IS NULL OR COALESCE(paid_sale.source_shift_id, NULLIF(paid_sale.payload->>'idTurno', '')::integer) = $5)
                      AND paid_sale.paid AND NOT paid_sale.cancelled AND paid_sale.closed_at IS NOT NULL), false),
                EXISTS (
                    SELECT 1 FROM sync_batches
                    WHERE branch_id = $1
                      AND reconciliation_ok = true
                      AND range_start <= $4
                      AND range_end >= $2)
            FROM sales
            WHERE branch_id = $1 AND business_date >= $4 AND business_date < $3
              AND ($5::integer IS NULL OR COALESCE(source_shift_id, NULLIF(payload->>'idTurno', '')::integer) = $5);
            """);
        command.Parameters.AddWithValue(meta.BranchId);
        command.Parameters.AddWithValue(start);
        command.Parameters.AddWithValue(end);
        command.Parameters.AddWithValue(previousStart);
        command.Parameters.AddWithValue((object?)meta.ShiftNumber ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);

        var sales = reader.GetDecimal(1);
        var previousSales = reader.GetBoolean(15) ? reader.GetDecimal(5) : (decimal?)null;
        var paymentBreakdownComplete = reader.GetBoolean(14);
        var expectedCash = paymentBreakdownComplete
            ? reader.GetDecimal(12) + reader.GetDecimal(9) + reader.GetDecimal(7) - reader.GetDecimal(8)
            : (decimal?)null;
        var cashDifference = expectedCash is null
            ? (decimal?)null
            : reader.GetDecimal(13) - expectedCash.Value;
        decimal? change = previousSales is > 0
            ? Math.Round((sales - previousSales.Value) / previousSales.Value * 100m, 1)
            : null;

        return new DashboardSummary(
            reader.GetInt64(0),
            sales,
            reader.GetDecimal(2),
            reader.GetDecimal(3),
            reader.GetInt64(4),
            reader.GetInt64(6),
            reader.GetDecimal(7),
            reader.GetDecimal(8),
            reader.GetDecimal(9),
            reader.GetDecimal(10),
            reader.GetDecimal(11),
            reader.GetDecimal(12),
            reader.GetDecimal(13),
            expectedCash,
            cashDifference,
            paymentBreakdownComplete,
            previousSales,
            change);
    }

    private async Task<IReadOnlyList<HourlySalesPoint>> GetHourlySalesAsync(
        DashboardMeta meta,
        CancellationToken ct)
    {
        var start = meta.Date.ToDateTime(TimeOnly.MinValue);
        await using var command = dataSource.CreateCommand("""
            SELECT EXTRACT(HOUR FROM closed_at)::int AS hour,
                   COALESCE(SUM(total), 0), COUNT(*)
            FROM sales
            WHERE branch_id = $1
              AND business_date >= $2 AND business_date < $3
              AND ($4::integer IS NULL OR COALESCE(source_shift_id, NULLIF(payload->>'idTurno', '')::integer) = $4)
              AND paid AND NOT cancelled AND closed_at IS NOT NULL
            GROUP BY 1
            ORDER BY 1;
            """);
        command.Parameters.AddWithValue(meta.BranchId);
        command.Parameters.AddWithValue(start);
        command.Parameters.AddWithValue(start.AddDays(1));
        command.Parameters.AddWithValue((object?)meta.ShiftNumber ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var points = new List<HourlySalesPoint>();
        while (await reader.ReadAsync(ct))
            points.Add(new HourlySalesPoint(reader.GetInt32(0), reader.GetDecimal(1), reader.GetInt64(2)));
        return points;
    }

    private async Task<SalesPage> GetTicketItemsAsync(
        DashboardMeta meta,
        int page,
        int pageSize,
        string? search,
        CancellationToken ct)
    {
        var start = meta.Date.ToDateTime(TimeOnly.MinValue);
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        await using var command = dataSource.CreateCommand("""
            SELECT source_folio,
                   payload->>'numCheque', business_date, closed_at, total, tip, paid, cancelled,
                   payload->>'mesa', payload->>'usuarioPago'
            FROM sales
            WHERE branch_id = $1
              AND business_date >= $2 AND business_date < $3
              AND ($4::integer IS NULL OR COALESCE(source_shift_id, NULLIF(payload->>'idTurno', '')::integer) = $4)
              AND ($5::text IS NULL OR source_folio::text ILIKE '%' || $5 || '%'
                   OR COALESCE(payload->>'numCheque', '') ILIKE '%' || $5 || '%')
            ORDER BY COALESCE(closed_at, business_date) DESC NULLS LAST, source_folio DESC
            LIMIT $6 OFFSET $7;
            """);
        command.Parameters.AddWithValue(meta.BranchId);
        command.Parameters.AddWithValue(start);
        command.Parameters.AddWithValue(start.AddDays(1));
        command.Parameters.AddWithValue((object?)meta.ShiftNumber ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)normalizedSearch ?? DBNull.Value);
        command.Parameters.AddWithValue(pageSize + 1);
        command.Parameters.AddWithValue((page - 1) * pageSize);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var items = new List<SalesTicketItem>();
        while (await reader.ReadAsync(ct)) items.Add(ReadTicket(reader));
        var hasMore = items.Count > pageSize;
        if (hasMore) items.RemoveAt(items.Count - 1);
        return new SalesPage(meta, items, page, pageSize, hasMore);
    }

    private async Task<IReadOnlyList<CancellationItem>> GetCancellationsAsync(
        DashboardMeta meta,
        int limit,
        CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT cancellation_date, source_folio, product_id,
                   payload->>'description', quantity, price, occurrences,
                   payload->>'user', payload->>'reason'
            FROM cancellation_summaries
            WHERE branch_id = $1 AND cancellation_date = $2::date
              AND ($3::integer IS NULL OR EXISTS (SELECT 1 FROM sales s
                  WHERE s.branch_id = cancellation_summaries.branch_id
                    AND s.source_folio = cancellation_summaries.source_folio
                    AND COALESCE(s.source_shift_id, NULLIF(s.payload->>'idTurno', '')::integer) = $3))
            ORDER BY cancellation_date DESC, updated_at DESC
            LIMIT $4;
            """);
        command.Parameters.AddWithValue(meta.BranchId);
        command.Parameters.AddWithValue(meta.Date.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue((object?)meta.ShiftNumber ?? DBNull.Value);
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var items = new List<CancellationItem>();
        while (await reader.ReadAsync(ct))
        {
            items.Add(new CancellationItem(
                reader.GetDateTime(0),
                ReadNullableInt64(reader, 1),
                ReadNullableString(reader, 2),
                ReadNullableString(reader, 3),
                ReadNullableDecimal(reader, 4),
                ReadNullableDecimal(reader, 5),
                reader.GetInt32(6),
                ReadNullableString(reader, 7),
                ReadNullableString(reader, 8)));
        }
        return items;
    }

    private async Task<CashMovementsPage> GetCashMovementItemsAsync(
        DashboardMeta meta,
        int page,
        int pageSize,
        int? type,
        string? search,
        CancellationToken ct)
    {
        var start = meta.Date.ToDateTime(TimeOnly.MinValue);
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        await using var command = dataSource.CreateCommand("""
            SELECT source_folio, movement_date, movement_type, amount,
                   payload->>'concepto', payload->>'referencia'
            FROM cash_movements
            WHERE branch_id = $1
              AND movement_date >= $2 AND movement_date < $3
              AND ($8::integer IS NULL OR COALESCE(source_shift_id, NULLIF(payload->>'idTurno', '')::integer) = $8)
              AND NOT cancelled
              AND ($4::integer IS NULL OR movement_type = $4)
              AND ($5::text IS NULL
                   OR source_folio::text ILIKE '%' || $5 || '%'
                   OR COALESCE(payload->>'concepto', '') ILIKE '%' || $5 || '%'
                   OR COALESCE(payload->>'referencia', '') ILIKE '%' || $5 || '%')
            ORDER BY movement_date DESC NULLS LAST, source_folio DESC
            LIMIT $6 OFFSET $7;
            """);
        command.Parameters.AddWithValue(meta.BranchId);
        command.Parameters.AddWithValue(start);
        command.Parameters.AddWithValue(start.AddDays(1));
        command.Parameters.AddWithValue((object?)type ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)normalizedSearch ?? DBNull.Value);
        command.Parameters.AddWithValue(pageSize + 1);
        command.Parameters.AddWithValue((page - 1) * pageSize);
        command.Parameters.AddWithValue((object?)meta.ShiftNumber ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var items = new List<CashMovementItem>();
        while (await reader.ReadAsync(ct))
        {
            items.Add(new CashMovementItem(
                reader.GetInt64(0),
                ReadNullableDateTime(reader, 1),
                reader.GetInt32(2),
                ReadNullableDecimal(reader, 3),
                ReadNullableString(reader, 4),
                ReadNullableString(reader, 5)));
        }
        var hasMore = items.Count > pageSize;
        if (hasMore) items.RemoveAt(items.Count - 1);
        return new CashMovementsPage(meta, items, page, pageSize, hasMore);
    }

    private SalesTicketItem ReadTicket(NpgsqlDataReader reader) => new(
        reader.GetInt64(0),
        ReadNullableString(reader, 1),
        ReadNullableDateTime(reader, 2),
        ReadNullableDateTime(reader, 3),
        ReadNullableDecimal(reader, 4),
        ReadNullableDecimal(reader, 5),
        reader.GetBoolean(6),
        reader.GetBoolean(7),
        ReadNullableString(reader, 8),
        ReadNullableString(reader, 9));

    private string GetFreshness(DateTime? lastSyncAt)
    {
        if (lastSyncAt is null) return "missing";
        return DateTime.UtcNow - lastSyncAt.Value.ToUniversalTime() > TimeSpan.FromMinutes(options.DashboardStaleMinutes)
            ? "stale"
            : "fresh";
    }

    internal static string GetCoverage(
        DateOnly date,
        string? batchId,
        DateTime? rangeStart,
        DateTime? rangeEnd,
        bool? reconciliationOk)
    {
        if (batchId is null || rangeStart is null || rangeEnd is null) return "missing";
        if (reconciliationOk != true) return "invalid";
        var start = date.ToDateTime(TimeOnly.MinValue);
        if (rangeStart.Value > start || rangeEnd.Value <= start) return "missing";
        return rangeEnd.Value >= start.AddDays(1) ? "complete" : "partial";
    }

    private static readonly DashboardSummary EmptySummary =
        new(null, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null, false, null, null);

    private static string? ReadNullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateTime? ReadNullableDateTime(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);

    private static decimal? ReadNullableDecimal(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);

    private static long? ReadNullableInt64(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    private static bool? ReadNullableBool(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);
}
