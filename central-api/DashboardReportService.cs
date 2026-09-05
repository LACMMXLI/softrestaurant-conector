using Npgsql;

namespace RestaurantAgent.CentralApi;

internal sealed record DashboardBranch(
    Guid BusinessId,
    string Code,
    string Name,
    string Timezone,
    DateTime? LastSyncAt,
    string Freshness,
    bool? ReconciliationOk,
    DateTime? RangeStart,
    DateTime? RangeEnd,
    DateTime? SyncRequestedAt);

internal sealed record BusinessDashboardSummary(
    long Tickets, decimal Sales, decimal AverageTicket, decimal Tips,
    long CancelledTickets, long CancelledLines, decimal CashIn, decimal CashOut,
    decimal CashSales, decimal CardSales, decimal OtherSales);

internal sealed record BusinessBranchContribution(
    string Code, string Name, long Tickets, decimal Sales, decimal AverageTicket,
    decimal ParticipationPercent, string Coverage);

internal sealed record BusinessDashboardResponse(
    Guid BusinessId, string BusinessName, DateOnly Date, string Coverage,
    int IncludedBranches, int TotalBranches, BusinessDashboardSummary Summary,
    IReadOnlyList<BusinessBranchContribution> Branches, TopProducts TopProducts);

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
    int? ShiftNumber,
    bool ShiftIsOpen);

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
    decimal? SalesChangePercent,
    long? OpenAccounts,
    decimal? OpenAccountsTotal,
    decimal? CurrentActivity);

internal sealed record OpenShiftSnapshotMetrics(
    long PaidTickets,
    decimal Sales,
    decimal Tips,
    decimal CashSales,
    decimal CardSales,
    decimal OtherSales,
    bool PaymentBreakdownComplete,
    long OpenAccounts,
    decimal OpenAccountsTotal);

internal sealed record HourlySalesPoint(int Hour, decimal Sales, long Tickets);

internal sealed record TopProductItem(string ProductId, string ProductName, string? GroupName,
    decimal Quantity, decimal Sales, int Rank);

internal sealed record TopProducts(IReadOnlyList<TopProductItem> Foods, IReadOnlyList<TopProductItem> Beverages);

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
    string? PaymentUser,
    bool Transient);

internal sealed record TransientAccountItem(
    long TempFolio,
    string? CheckNumber,
    DateTime? OpenedAt,
    decimal? Total,
    decimal? Tip,
    bool Paid,
    string? Table,
    string? Waiter,
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

internal sealed record ProductCancellationReportItem(string EventKey, string SourceKind, DateTime? CancelledAt, long? Folio, long? TempFolio, string? ProductId, string? Description, decimal? Quantity, decimal? UnitPrice, decimal? Amount, string? User, string? Reason, string? ReasonDescription, int? ShiftId, string? Area, string? Company, string? AccountLabel, string CorrelationStatus, string AccountStatus, DateTime? CorrelationEventAt, DateTime? OpenedAt, DateTime? ClosedAt, decimal? FinalTotal, int SourceDuplicateCount);
internal sealed record CancellationMetric(string Label, decimal Amount, decimal Quantity);
internal sealed record ProductCancellationReport(DashboardMeta Meta, decimal TotalAmount, decimal TotalQuantity, IReadOnlyList<CancellationMetric> ByEmployee, IReadOnlyList<CancellationMetric> TopProducts, IReadOnlyList<CancellationMetric> ByShift, IReadOnlyList<CancellationMetric> ByDay, IReadOnlyList<ProductCancellationReportItem> Items, int Page, int PageSize, bool HasMore);

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
    IReadOnlyList<TransientAccountItem> OpenAccounts,
    TopProducts TopProducts,
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
            SELECT b.business_id, b.code, b.name, b.timezone, b.last_sync_at,
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
            // SELECT ordinal 3 is b.timezone (text); the timestamp begins at ordinal 4.
            var lastSyncAt = ReadNullableDateTime(reader, 4);
            branches.Add(new DashboardBranch(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                lastSyncAt, GetFreshness(lastSyncAt),
                ReadNullableBool(reader, 5), ReadNullableDateTime(reader, 6),
                ReadNullableDateTime(reader, 7), ReadNullableDateTime(reader, 8)));
        }
        return branches;
    }

    /// <summary>
    /// Consolidado calculado directamente sobre los hechos sincronizados. Solo incorpora una
    /// sucursal cuando su lote conciliado cubre por completo la fecha solicitada; así una
    /// ausencia de sincronización nunca se presenta como venta cero.
    /// </summary>
    public async Task<BusinessDashboardResponse?> GetBusinessHomeAsync(
        DashboardUser user, Guid businessId, DateOnly date, CancellationToken ct)
    {
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        await using var command = dataSource.CreateCommand("""
            WITH scoped_branches AS (
                SELECT b.id, b.code, b.name,
                       EXISTS (SELECT 1 FROM sync_batches sb WHERE sb.branch_id = b.id
                         AND sb.reconciliation_ok AND sb.range_start <= $3 AND sb.range_end >= $4) AS covered
                FROM branches b
                WHERE b.business_id = $1 AND b.active
                  AND EXISTS (SELECT 1 FROM business_members bm WHERE bm.business_id = b.business_id AND bm.user_id = $2)
            ), eligible AS (SELECT * FROM scoped_branches WHERE covered),
            sales_by_branch AS (
                SELECT e.id, COUNT(s.*) FILTER (WHERE s.paid AND NOT s.cancelled AND s.closed_at IS NOT NULL) tickets,
                       COALESCE(SUM(s.total) FILTER (WHERE s.paid AND NOT s.cancelled AND s.closed_at IS NOT NULL), 0) sales,
                       COALESCE(SUM(s.tip) FILTER (WHERE s.paid AND NOT s.cancelled AND s.closed_at IS NOT NULL), 0) tips,
                       COUNT(s.*) FILTER (WHERE s.cancelled) cancelled_tickets
                FROM eligible e LEFT JOIN sales s ON s.branch_id = e.id AND s.business_date >= $3 AND s.business_date < $4
                GROUP BY e.id
            ), payments AS (
                SELECT sp.branch_id,
                       COALESCE(SUM(sp.amount * COALESCE(NULLIF(sp.exchange_rate, 0), 1)) FILTER (WHERE NULLIF(sp.payload->>'tipoFormaDePago', '')::integer = 1),0) cash_sales,
                       COALESCE(SUM(sp.amount * COALESCE(NULLIF(sp.exchange_rate, 0), 1)) FILTER (WHERE NULLIF(sp.payload->>'tipoFormaDePago', '')::integer = 2),0) card_sales,
                       COALESCE(SUM(sp.amount * COALESCE(NULLIF(sp.exchange_rate, 0), 1)) FILTER (WHERE NULLIF(sp.payload->>'tipoFormaDePago', '')::integer IN (3,4)),0) other_sales
                FROM sale_payments sp JOIN sales s ON s.branch_id=sp.branch_id AND s.source_folio=sp.source_folio
                JOIN eligible e ON e.id=sp.branch_id
                WHERE s.business_date >= $3 AND s.business_date < $4 AND s.paid AND NOT s.cancelled AND s.closed_at IS NOT NULL
                GROUP BY sp.branch_id
            ), movements AS (
                SELECT cm.branch_id,
                  COALESCE(SUM(cm.amount) FILTER (WHERE cm.movement_type=2 AND NOT cm.cancelled),0) cash_in,
                  COALESCE(SUM(cm.amount) FILTER (WHERE cm.movement_type=1 AND NOT cm.cancelled),0) cash_out
                FROM cash_movements cm JOIN eligible e ON e.id=cm.branch_id
                WHERE cm.movement_date >= $3 AND cm.movement_date < $4 GROUP BY cm.branch_id
            ), cancellations AS (
                SELECT cs.branch_id, COALESCE(SUM(cs.occurrences),0) cancelled_lines
                FROM cancellation_summaries cs JOIN eligible e ON e.id=cs.branch_id
                WHERE cs.cancellation_date=$3::date GROUP BY cs.branch_id
            )
            SELECT b.name, (SELECT COUNT(*) FROM scoped_branches), (SELECT COUNT(*) FROM eligible),
                   e.code, e.name, COALESCE(sb.tickets,0), COALESCE(sb.sales,0), COALESCE(sb.tips,0), COALESCE(sb.cancelled_tickets,0),
                   COALESCE(c.cancelled_lines,0), COALESCE(m.cash_in,0), COALESCE(m.cash_out,0),
                   COALESCE(p.cash_sales,0), COALESCE(p.card_sales,0), COALESCE(p.other_sales,0)
            FROM businesses b LEFT JOIN eligible e ON true
            LEFT JOIN sales_by_branch sb ON sb.id=e.id LEFT JOIN payments p ON p.branch_id=e.id
            LEFT JOIN movements m ON m.branch_id=e.id LEFT JOIN cancellations c ON c.branch_id=e.id
            WHERE b.id=$1
              AND EXISTS (SELECT 1 FROM business_members bm WHERE bm.business_id=b.id AND bm.user_id=$2)
            ORDER BY e.name;
            """);
        command.Parameters.AddWithValue(businessId); command.Parameters.AddWithValue(user.Id);
        command.Parameters.AddWithValue(start); command.Parameters.AddWithValue(end);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var name = reader.GetString(0); var total = reader.GetInt32(1); var included = reader.GetInt32(2);
        var branches = new List<BusinessBranchContribution>();
        long tickets=0, cancelledTickets=0, cancelledLines=0; decimal sales=0,tips=0,cashIn=0,cashOut=0,cashSales=0,cardSales=0,otherSales=0;
        do {
            if (reader.IsDBNull(3)) continue;
            var branchTickets=reader.GetInt64(5); var branchSales=reader.GetDecimal(6);
            tickets += branchTickets; sales += branchSales; tips += reader.GetDecimal(7); cancelledTickets += reader.GetInt64(8); cancelledLines += reader.GetInt64(9);
            cashIn += reader.GetDecimal(10); cashOut += reader.GetDecimal(11); cashSales += reader.GetDecimal(12); cardSales += reader.GetDecimal(13); otherSales += reader.GetDecimal(14);
            branches.Add(new BusinessBranchContribution(reader.GetString(3), reader.GetString(4), branchTickets, branchSales,
                branchTickets > 0 ? branchSales / branchTickets : 0, 0, "complete"));
        } while (await reader.ReadAsync(ct));
        branches = branches.Select(x => x with { ParticipationPercent = sales > 0 ? Math.Round(x.Sales / sales * 100m, 1) : 0 }).OrderByDescending(x => x.Sales).ToList();
        var products = await GetBusinessTopProductsAsync(user, businessId, start, end, ct);
        var coverage = included == 0 ? "missing" : included == total ? "complete" : "partial";
        return new BusinessDashboardResponse(businessId, name, date, coverage, included, total,
            new BusinessDashboardSummary(tickets, sales, tickets > 0 ? sales/tickets : 0, tips, cancelledTickets, cancelledLines, cashIn, cashOut, cashSales, cardSales, otherSales), branches, products);
    }

    private async Task<TopProducts> GetBusinessTopProductsAsync(DashboardUser user, Guid businessId, DateTime start, DateTime end, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            WITH eligible AS (SELECT b.id FROM branches b WHERE b.business_id=$1 AND b.active AND EXISTS (SELECT 1 FROM business_members bm WHERE bm.business_id=b.business_id AND bm.user_id=$2)
              AND EXISTS (SELECT 1 FROM sync_batches sb WHERE sb.branch_id=b.id AND sb.reconciliation_ok AND sb.range_start <= $3 AND sb.range_end >= $4)),
            totals AS (SELECT COALESCE(NULLIF(p.description,''),NULLIF(l.payload->>'descripcionProducto',''),l.product_id) product_name, MAX(p.group_name) group_name,p.classification,
              SUM(l.quantity) quantity,SUM(GREATEST(l.quantity*l.price-COALESCE(NULLIF(l.payload->>'descuento','')::numeric,0),0)) sales
              FROM sale_lines l JOIN eligible e ON e.id=l.branch_id JOIN products p ON p.branch_id=l.branch_id AND p.product_id=l.product_id
              WHERE COALESCE(l.quantity,0)>0 AND COALESCE(l.price,0)>0 AND p.classification IN (1,2) AND EXISTS (SELECT 1 FROM sales s WHERE s.branch_id=l.branch_id AND s.source_folio=l.source_folio AND s.business_date >= $3 AND s.business_date < $4 AND s.paid AND NOT s.cancelled AND s.closed_at IS NOT NULL)
              GROUP BY COALESCE(NULLIF(p.description,''),NULLIF(l.payload->>'descripcionProducto',''),l.product_id),p.classification), ranked AS (SELECT *,ROW_NUMBER() OVER(PARTITION BY classification ORDER BY quantity DESC,sales DESC,product_name) rank FROM totals)
            SELECT product_name,group_name,classification,quantity,sales,rank FROM ranked WHERE rank<=20 ORDER BY classification,rank;
            """);
        command.Parameters.AddWithValue(businessId); command.Parameters.AddWithValue(user.Id); command.Parameters.AddWithValue(start); command.Parameters.AddWithValue(end);
        var foods=new List<TopProductItem>(); var beverages=new List<TopProductItem>(); await using var reader=await command.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct)){ var item=new TopProductItem(reader.GetString(0),reader.GetString(0),ReadNullableString(reader,1),reader.GetDecimal(3),reader.GetDecimal(4),checked((int)reader.GetInt64(5))); if(reader.GetInt32(2)==1) beverages.Add(item); else foods.Add(item); }
        return new TopProducts(foods,beverages);
    }

    public async Task<IReadOnlyList<DashboardShift>> GetShiftsAsync(
        DashboardUser user, string branchCode, DateOnly oldestDate, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT s.source_shift_id,
                   COALESCE(NULLIF(s.payload->>'idTurno', '')::integer, s.source_shift_id),
                   s.opened_at, s.closed_at, s.payload->>'cajero', s.closed_at IS NULL
            FROM shifts s
            INNER JOIN branches b ON b.id = s.branch_id
            WHERE b.active = true AND b.code = $1
              AND EXISTS (SELECT 1 FROM business_members bm
                          WHERE bm.user_id = $2 AND bm.business_id = b.business_id)
              AND (s.closed_at IS NULL OR s.opened_at >= $3)
            ORDER BY (s.closed_at IS NULL) DESC, s.opened_at DESC NULLS LAST, s.source_shift_id DESC;
            """);
        command.Parameters.AddWithValue(branchCode);
        command.Parameters.AddWithValue(user.Id);
        command.Parameters.AddWithValue(oldestDate.ToDateTime(TimeOnly.MinValue));
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
                new TopProducts([], []),
                [],
                []);
        }

        var summaryTask = GetSummaryAsync(meta, ct);
        var hourlyTask = GetHourlySalesAsync(meta, ct);
        var ticketsTask = GetTicketItemsAsync(meta, page: 1, pageSize: 6, search: null, ct);
        var openAccountsTask = GetTransientAccountItemsAsync(meta, limit: 20, ct);
        var topProductsTask = GetTopProductsAsync(meta, limitPerCategory: 20, ct);
        var cancellationsTask = GetCancellationsAsync(meta, limit: 5, ct);
        var cashTask = GetCashMovementItemsAsync(meta, page: 1, pageSize: 5, type: null, search: null, ct);
        await Task.WhenAll(summaryTask, hourlyTask, ticketsTask, openAccountsTask, topProductsTask, cancellationsTask, cashTask);

        return new DashboardHomeResponse(
            meta,
            await summaryTask,
            await hourlyTask,
            (await ticketsTask).Items,
            await openAccountsTask,
            await topProductsTask,
            await cancellationsTask,
            (await cashTask).Items);
    }

    private async Task<TopProducts> GetTopProductsAsync(DashboardMeta meta, int limitPerCategory, CancellationToken ct)
    {
        var start = meta.Date.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        await using var command = dataSource.CreateCommand("""
            WITH eligible_lines AS (
                SELECT l.branch_id, l.product_id, l.quantity, l.price, l.payload
                FROM sale_lines l
                WHERE l.branch_id = $1 AND COALESCE(l.quantity, 0) > 0 AND COALESCE(l.price, 0) > 0
                  AND EXISTS (
                      SELECT 1 FROM sales s
                      WHERE s.branch_id = l.branch_id AND s.source_folio = l.source_folio
                        AND s.paid AND NOT s.cancelled AND s.closed_at IS NOT NULL
                        AND (($4::integer IS NULL AND s.business_date >= $2 AND s.business_date < $3)
                             OR ($4::integer IS NOT NULL AND COALESCE(s.source_shift_id, NULLIF(s.payload->>'idTurno', '')::integer) = $4)))
                UNION ALL
                SELECT tl.branch_id, tl.product_id, tl.quantity, tl.price, tl.payload
                FROM transient_sale_lines tl
                INNER JOIN transient_sales ts
                  ON ts.branch_id = tl.branch_id AND ts.idempotency_key = tl.header_key
                WHERE tl.branch_id = $1 AND $4::integer IS NOT NULL
                  AND ts.source_shift_id = $4
                  AND ts.paid AND NOT ts.cancelled AND ts.closed_at IS NOT NULL
                  AND COALESCE(tl.quantity, 0) > 0 AND COALESCE(tl.price, 0) > 0
                  AND NOT EXISTS (
                      SELECT 1 FROM sales s
                      WHERE s.branch_id = ts.branch_id
                        AND s.source_shift_id = ts.source_shift_id
                        AND s.source_temp_folio = ts.source_temp_folio)
            ), totals AS (
                SELECT l.product_id,
                       COALESCE(NULLIF(p.description, ''), NULLIF(l.payload->>'descripcionProducto', ''), l.product_id) AS product_name,
                       p.group_name, p.classification,
                       SUM(COALESCE(l.quantity, 0)) AS quantity,
                       SUM(GREATEST(COALESCE(l.quantity, 0) * COALESCE(l.price, 0)
                           - COALESCE(NULLIF(l.payload->>'descuento', '')::numeric, 0), 0)) AS sales
                FROM eligible_lines l
                INNER JOIN products p ON p.branch_id = l.branch_id AND p.product_id = l.product_id
                WHERE p.classification IN (1, 2)
                GROUP BY l.product_id,
                         COALESCE(NULLIF(p.description, ''), NULLIF(l.payload->>'descripcionProducto', ''), l.product_id),
                         p.group_name, p.classification
            ), ranked AS (
                SELECT *, ROW_NUMBER() OVER (PARTITION BY classification ORDER BY quantity DESC, sales DESC, product_name) AS rank
                FROM totals
            )
            SELECT product_id, product_name, group_name, classification, quantity, sales, rank
            FROM ranked WHERE rank <= $5 ORDER BY classification, rank;
            """);
        command.Parameters.AddWithValue(meta.BranchId);
        command.Parameters.AddWithValue(start);
        command.Parameters.AddWithValue(end);
        command.Parameters.AddWithValue((object?)meta.ShiftNumber ?? DBNull.Value);
        command.Parameters.AddWithValue(limitPerCategory);
        var foods = new List<TopProductItem>();
        var beverages = new List<TopProductItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var item = new TopProductItem(reader.GetString(0), reader.GetString(1), ReadNullableString(reader, 2),
                reader.GetDecimal(4), reader.GetDecimal(5), checked((int)reader.GetInt64(6)));
            if (reader.GetInt32(3) == 1) beverages.Add(item); else foods.Add(item);
        }
        return new TopProducts(foods, beverages);
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
        DateOnly oldestDate,
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
                   payload->>'mesa', payload->>'usuarioPago', false AS transient, payload->>'estacion',
                   payload->>'idAreaRestaurant', payload->>'idMesero',
                   payload->>'razonCancelado', payload->>'usuarioCancelo'
            FROM sales
            WHERE branch_id = $1 AND source_folio = $2 AND business_date >= $3
            ORDER BY updated_at DESC
            LIMIT 1;
            """))
        {
            command.Parameters.AddWithValue(branch.Value.Id);
            command.Parameters.AddWithValue(folio);
            command.Parameters.AddWithValue(oldestDate.ToDateTime(TimeOnly.MinValue));
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            ticket = ReadTicket(reader);
            station = ReadNullableString(reader, 11);
            restaurantArea = ReadNullableString(reader, 12);
            waiterId = ReadNullableString(reader, 13);
            cancellationReason = ReadNullableString(reader, 14);
            cancelledBy = ReadNullableString(reader, 15);
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

    public async Task<TicketDetail?> GetOpenAccountAsync(
        DashboardUser user,
        string branchCode,
        long tempFolio,
        CancellationToken ct)
    {
        var branch = await GetBranchIdentityAsync(user, branchCode, ct);
        if (branch is null) return null;

        SalesTicketItem? ticket = null;
        string? station = null;
        string? restaurantArea = null;
        string? waiterId = null;
        string? accountKey = null;

        await using (var command = dataSource.CreateCommand("""
            SELECT idempotency_key, source_temp_folio, check_number, opened_at, closed_at, total, tip,
                   paid, cancelled, payload->>'mesa', payload->>'usuarioPago', true AS transient,
                   payload->>'estacion', payload->>'idAreaRestaurant', payload->>'idMesero'
            FROM transient_sales
            WHERE branch_id = $1 AND source_temp_folio = $2
              AND NOT cancelled
              AND NOT EXISTS (
                  SELECT 1 FROM sales s
                  WHERE s.branch_id = transient_sales.branch_id
                    AND s.source_shift_id = transient_sales.source_shift_id
                    AND s.source_temp_folio = transient_sales.source_temp_folio)
            ORDER BY updated_at DESC
            LIMIT 1;
            """))
        {
            command.Parameters.AddWithValue(branch.Value.Id);
            command.Parameters.AddWithValue(tempFolio);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            accountKey = reader.GetString(0);
            ticket = new SalesTicketItem(
                reader.GetInt64(1), ReadNullableString(reader, 2), ReadNullableDateTime(reader, 3),
                ReadNullableDateTime(reader, 4), ReadNullableDecimal(reader, 5), ReadNullableDecimal(reader, 6),
                reader.GetBoolean(7), reader.GetBoolean(8), ReadNullableString(reader, 9),
                ReadNullableString(reader, 10), reader.GetBoolean(11));
            station = ReadNullableString(reader, 12);
            restaurantArea = ReadNullableString(reader, 13);
            waiterId = ReadNullableString(reader, 14);
        }

        var lines = new List<TicketLineItem>();
        await using (var command = dataSource.CreateCommand("""
            SELECT product_id, NULLIF(payload->>'descripcionProducto', ''), quantity, price,
                   NULLIF(payload->>'descuento', '')::numeric, payload->>'comentario'
            FROM transient_sale_lines
            WHERE branch_id = $1 AND header_key = $2
            ORDER BY NULLIF(payload->>'hora', '')::timestamp NULLS LAST, idempotency_key;
            """))
        {
            command.Parameters.AddWithValue(branch.Value.Id);
            command.Parameters.AddWithValue(accountKey!);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                lines.Add(new TicketLineItem(
                    ReadNullableString(reader, 0), ReadNullableString(reader, 1), ReadNullableDecimal(reader, 2),
                    ReadNullableDecimal(reader, 3), ReadNullableDecimal(reader, 4), ReadNullableString(reader, 5)));
            }
        }

        var payments = new List<TicketPaymentItem>();
        await using (var command = dataSource.CreateCommand("""
            SELECT payment_method, NULLIF(payload->>'descripcionFormaDePago', ''),
                   NULLIF(payload->>'tipoFormaDePago', '')::integer,
                   amount, tip, exchange_rate, payload->>'cardBrand'
            FROM transient_sale_payments
            WHERE branch_id = $1 AND header_key = $2
            ORDER BY idempotency_key;
            """))
        {
            command.Parameters.AddWithValue(branch.Value.Id);
            command.Parameters.AddWithValue(accountKey!);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                payments.Add(new TicketPaymentItem(
                    ReadNullableString(reader, 0), ReadNullableString(reader, 1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    ReadNullableDecimal(reader, 3), ReadNullableDecimal(reader, 4),
                    ReadNullableDecimal(reader, 5), ReadNullableString(reader, 6)));
            }
        }

        return new TicketDetail(ticket, station, restaurantArea, waiterId, null, null, lines, payments);
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
                   selected_shift.source_shift_id, selected_shift.shift_number, selected_shift.is_open
            FROM branches b
            LEFT JOIN LATERAL (
                SELECT s.source_shift_id,
                       NULLIF(s.payload->>'idTurno', '')::integer AS shift_number,
                       s.closed_at IS NULL AS is_open
                FROM shifts s
                WHERE s.branch_id = b.id AND s.source_shift_id = $5
                LIMIT 1
            ) selected_shift ON true
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
        int? selectedShiftId = reader.IsDBNull(9) ? null : reader.GetInt32(9);
        int? selectedShiftNumber = reader.IsDBNull(10) ? null : reader.GetInt32(10);
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
            ResolveBusinessShiftNumber(selectedShiftId, selectedShiftNumber),
            !reader.IsDBNull(11) && reader.GetBoolean(11));
    }

    internal static int? ResolveBusinessShiftNumber(int? sourceShiftId, int? payloadShiftNumber) =>
        payloadShiftNumber ?? sourceShiftId;

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
                    WHERE (($5::integer IS NULL AND business_date >= $2 AND business_date < $3)
                           OR ($5::integer IS NOT NULL AND COALESCE(source_shift_id, NULLIF(payload->>'idTurno', '')::integer) = $5))
                      AND paid AND NOT cancelled AND closed_at IS NOT NULL),
                COALESCE(SUM(total) FILTER (
                    WHERE (($5::integer IS NULL AND business_date >= $2 AND business_date < $3)
                           OR ($5::integer IS NOT NULL AND COALESCE(source_shift_id, NULLIF(payload->>'idTurno', '')::integer) = $5))
                      AND paid AND NOT cancelled AND closed_at IS NOT NULL), 0),
                COALESCE(AVG(total) FILTER (
                    WHERE (($5::integer IS NULL AND business_date >= $2 AND business_date < $3)
                           OR ($5::integer IS NOT NULL AND COALESCE(source_shift_id, NULLIF(payload->>'idTurno', '')::integer) = $5))
                      AND paid AND NOT cancelled AND closed_at IS NOT NULL), 0),
                COALESCE(SUM(tip) FILTER (
                    WHERE (($5::integer IS NULL AND business_date >= $2 AND business_date < $3)
                           OR ($5::integer IS NOT NULL AND COALESCE(source_shift_id, NULLIF(payload->>'idTurno', '')::integer) = $5))
                      AND paid AND NOT cancelled AND closed_at IS NOT NULL), 0),
                COUNT(*) FILTER (
                    WHERE (($5::integer IS NULL AND business_date >= $2 AND business_date < $3)
                           OR ($5::integer IS NOT NULL AND COALESCE(source_shift_id, NULLIF(payload->>'idTurno', '')::integer) = $5))
                      AND cancelled),
                COALESCE(SUM(total) FILTER (
                    WHERE $5::integer IS NULL AND business_date >= $4 AND business_date < $2
                      AND paid AND NOT cancelled AND closed_at IS NOT NULL), 0),
                COALESCE((SELECT SUM(occurrences) FROM cancellation_summaries
                    WHERE branch_id = $1
                      AND (($5::integer IS NULL AND cancellation_date = $2::date)
                           OR ($5::integer IS NOT NULL AND EXISTS (
                               SELECT 1 FROM sales cancelled_sale
                               WHERE cancelled_sale.branch_id = cancellation_summaries.branch_id
                                 AND cancelled_sale.source_folio = cancellation_summaries.source_folio
                                 AND COALESCE(cancelled_sale.source_shift_id, NULLIF(cancelled_sale.payload->>'idTurno', '')::integer) = $5)))), 0),
                COALESCE((SELECT SUM(amount) FROM cash_movements
                    WHERE branch_id = $1
                      AND (($5::integer IS NULL AND movement_date >= $2 AND movement_date < $3)
                           OR ($5::integer IS NOT NULL AND COALESCE(source_shift_id, NULLIF(payload->>'idTurno', '')::integer) = $5))
                      AND movement_type = 2 AND NOT cancelled), 0),
                COALESCE((SELECT SUM(amount) FROM cash_movements
                    WHERE branch_id = $1
                      AND (($5::integer IS NULL AND movement_date >= $2 AND movement_date < $3)
                           OR ($5::integer IS NOT NULL AND COALESCE(source_shift_id, NULLIF(payload->>'idTurno', '')::integer) = $5))
                      AND movement_type = 1 AND NOT cancelled), 0),
                COALESCE((SELECT SUM(sp.amount * COALESCE(NULLIF(sp.exchange_rate, 0), 1))
                    FROM sale_payments sp
                    INNER JOIN sales paid_sale
                      ON paid_sale.branch_id = sp.branch_id
                     AND paid_sale.source_folio = sp.source_folio
                    WHERE sp.branch_id = $1
                      AND (($5::integer IS NULL AND paid_sale.business_date >= $2 AND paid_sale.business_date < $3)
                           OR ($5::integer IS NOT NULL AND COALESCE(paid_sale.source_shift_id, NULLIF(paid_sale.payload->>'idTurno', '')::integer) = $5))
                      AND paid_sale.paid AND NOT paid_sale.cancelled AND paid_sale.closed_at IS NOT NULL
                      AND NULLIF(sp.payload->>'tipoFormaDePago', '')::integer = 1), 0),
                COALESCE((SELECT SUM(sp.amount * COALESCE(NULLIF(sp.exchange_rate, 0), 1))
                    FROM sale_payments sp
                    INNER JOIN sales paid_sale
                      ON paid_sale.branch_id = sp.branch_id
                     AND paid_sale.source_folio = sp.source_folio
                    WHERE sp.branch_id = $1
                      AND (($5::integer IS NULL AND paid_sale.business_date >= $2 AND paid_sale.business_date < $3)
                           OR ($5::integer IS NOT NULL AND COALESCE(paid_sale.source_shift_id, NULLIF(paid_sale.payload->>'idTurno', '')::integer) = $5))
                      AND paid_sale.paid AND NOT paid_sale.cancelled AND paid_sale.closed_at IS NOT NULL
                      AND NULLIF(sp.payload->>'tipoFormaDePago', '')::integer = 2), 0),
                COALESCE((SELECT SUM(sp.amount * COALESCE(NULLIF(sp.exchange_rate, 0), 1))
                    FROM sale_payments sp
                    INNER JOIN sales paid_sale
                      ON paid_sale.branch_id = sp.branch_id
                     AND paid_sale.source_folio = sp.source_folio
                    WHERE sp.branch_id = $1
                      AND (($5::integer IS NULL AND paid_sale.business_date >= $2 AND paid_sale.business_date < $3)
                           OR ($5::integer IS NOT NULL AND COALESCE(paid_sale.source_shift_id, NULLIF(paid_sale.payload->>'idTurno', '')::integer) = $5))
                      AND paid_sale.paid AND NOT paid_sale.cancelled AND paid_sale.closed_at IS NOT NULL
                      AND NULLIF(sp.payload->>'tipoFormaDePago', '')::integer IN (3, 4)), 0),
                COALESCE((SELECT SUM(NULLIF(payload->>'fondo', '')::numeric)
                    FROM shifts
                    WHERE branch_id = $1
                      AND (($5::integer IS NULL AND opened_at >= $2 AND opened_at < $3)
                           OR ($5::integer IS NOT NULL AND NULLIF(payload->>'idTurno', '')::integer = $5))), 0),
                COALESCE((SELECT SUM(NULLIF(payload->>'efectivo', '')::numeric)
                    FROM shifts
                    WHERE branch_id = $1
                      AND (($5::integer IS NULL AND opened_at >= $2 AND opened_at < $3)
                           OR ($5::integer IS NOT NULL AND NULLIF(payload->>'idTurno', '')::integer = $5))), 0),
                COALESCE((SELECT bool_and(NULLIF(sp.payload->>'tipoFormaDePago', '') IS NOT NULL)
                    FROM sale_payments sp
                    INNER JOIN sales paid_sale
                      ON paid_sale.branch_id = sp.branch_id
                     AND paid_sale.source_folio = sp.source_folio
                    WHERE sp.branch_id = $1
                      AND (($5::integer IS NULL AND paid_sale.business_date >= $2 AND paid_sale.business_date < $3)
                           OR ($5::integer IS NOT NULL AND COALESCE(paid_sale.source_shift_id, NULLIF(paid_sale.payload->>'idTurno', '')::integer) = $5))
                      AND paid_sale.paid AND NOT paid_sale.cancelled AND paid_sale.closed_at IS NOT NULL), false),
                EXISTS (
                    SELECT 1 FROM sync_batches
                    WHERE branch_id = $1
                      AND reconciliation_ok = true
                      AND range_start <= $4
                      AND range_end >= $2)
            FROM sales
            WHERE branch_id = $1
              AND (($5::integer IS NULL AND business_date >= $4 AND business_date < $3)
                   OR ($5::integer IS NOT NULL AND COALESCE(source_shift_id, NULLIF(payload->>'idTurno', '')::integer) = $5));
            """);
        command.Parameters.AddWithValue(meta.BranchId);
        command.Parameters.AddWithValue(start);
        command.Parameters.AddWithValue(end);
        command.Parameters.AddWithValue(previousStart);
        command.Parameters.AddWithValue((object?)meta.ShiftNumber ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);

        var historicalTickets = reader.GetInt64(0);
        var historicalSales = reader.GetDecimal(1);
        var historicalTips = reader.GetDecimal(3);
        var historicalCashSales = reader.GetDecimal(9);
        var historicalCardSales = reader.GetDecimal(10);
        var historicalOtherSales = reader.GetDecimal(11);
        var openShift = await GetOpenShiftSnapshotMetricsAsync(meta, ct);
        var tickets = historicalTickets + openShift.PaidTickets;
        var sales = historicalSales + openShift.Sales;
        var tips = historicalTips + openShift.Tips;
        var cashSales = historicalCashSales + openShift.CashSales;
        var cardSales = historicalCardSales + openShift.CardSales;
        var otherSales = historicalOtherSales + openShift.OtherSales;
        var previousSales = reader.GetBoolean(15) ? reader.GetDecimal(5) : (decimal?)null;
        var paymentBreakdownComplete = tickets > 0
            && (historicalTickets == 0 || reader.GetBoolean(14))
            && (openShift.PaidTickets == 0 || openShift.PaymentBreakdownComplete);
        var expectedCash = paymentBreakdownComplete
            ? reader.GetDecimal(12) + cashSales + reader.GetDecimal(7) - reader.GetDecimal(8)
            : (decimal?)null;
        var declaredCash = meta.ShiftIsOpen ? (decimal?)null : reader.GetDecimal(13);
        var cashDifference = expectedCash is null || declaredCash is null
            ? (decimal?)null
            : declaredCash.Value - expectedCash.Value;
        decimal? change = previousSales is > 0
            ? Math.Round((sales - previousSales.Value) / previousSales.Value * 100m, 1)
            : null;

        return new DashboardSummary(
            tickets,
            sales,
            tickets > 0 ? sales / tickets : 0,
            tips,
            reader.GetInt64(4),
            reader.GetInt64(6),
            reader.GetDecimal(7),
            reader.GetDecimal(8),
            cashSales,
            cardSales,
            otherSales,
            reader.GetDecimal(12),
            declaredCash,
            expectedCash,
            cashDifference,
            paymentBreakdownComplete,
            previousSales,
            change,
            openShift.OpenAccounts,
            openShift.OpenAccountsTotal,
            sales + openShift.OpenAccountsTotal);
    }

    private async Task<OpenShiftSnapshotMetrics> GetOpenShiftSnapshotMetricsAsync(
        DashboardMeta meta,
        CancellationToken ct)
    {
        if (!meta.ShiftIsOpen || meta.ShiftNumber is null)
            return new OpenShiftSnapshotMetrics(0, 0, 0, 0, 0, 0, false, 0, 0);
        await using var command = dataSource.CreateCommand("""
            WITH active_transient AS (
                SELECT ts.*
                FROM transient_sales ts
                WHERE ts.branch_id = $1
                  AND ts.source_shift_id = $2
                  AND NOT ts.cancelled
                  AND NOT EXISTS (
                      SELECT 1 FROM sales s
                      WHERE s.branch_id = ts.branch_id
                        AND s.source_shift_id = ts.source_shift_id
                        AND s.source_temp_folio = ts.source_temp_folio)
            ), paid_transient AS (
                SELECT * FROM active_transient WHERE paid AND closed_at IS NOT NULL
            )
            SELECT
                (SELECT COUNT(*) FROM paid_transient),
                COALESCE((SELECT SUM(total) FROM paid_transient), 0),
                COALESCE((SELECT SUM(tip) FROM paid_transient), 0),
                COALESCE((SELECT SUM(tp.amount * COALESCE(NULLIF(tp.exchange_rate, 0), 1))
                    FROM transient_sale_payments tp
                    INNER JOIN paid_transient pts ON pts.branch_id = tp.branch_id AND pts.idempotency_key = tp.header_key
                    WHERE NULLIF(tp.payload->>'tipoFormaDePago', '')::integer = 1), 0),
                COALESCE((SELECT SUM(tp.amount * COALESCE(NULLIF(tp.exchange_rate, 0), 1))
                    FROM transient_sale_payments tp
                    INNER JOIN paid_transient pts ON pts.branch_id = tp.branch_id AND pts.idempotency_key = tp.header_key
                    WHERE NULLIF(tp.payload->>'tipoFormaDePago', '')::integer = 2), 0),
                COALESCE((SELECT SUM(tp.amount * COALESCE(NULLIF(tp.exchange_rate, 0), 1))
                    FROM transient_sale_payments tp
                    INNER JOIN paid_transient pts ON pts.branch_id = tp.branch_id AND pts.idempotency_key = tp.header_key
                    WHERE NULLIF(tp.payload->>'tipoFormaDePago', '')::integer IN (3, 4)), 0),
                NOT EXISTS (
                    SELECT 1 FROM paid_transient pts
                    WHERE NOT EXISTS (
                        SELECT 1 FROM transient_sale_payments tp
                        WHERE tp.branch_id = pts.branch_id AND tp.header_key = pts.idempotency_key))
                AND NOT EXISTS (
                    SELECT 1
                    FROM transient_sale_payments tp
                    INNER JOIN paid_transient pts ON pts.branch_id = tp.branch_id AND pts.idempotency_key = tp.header_key
                    WHERE NULLIF(tp.payload->>'tipoFormaDePago', '') IS NULL),
                (SELECT COUNT(*) FROM active_transient WHERE NOT paid),
                COALESCE((SELECT SUM(total) FROM active_transient WHERE NOT paid), 0);
            """);
        command.Parameters.AddWithValue(meta.BranchId);
        command.Parameters.AddWithValue(meta.ShiftNumber.Value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new OpenShiftSnapshotMetrics(
            reader.GetInt64(0), reader.GetDecimal(1), reader.GetDecimal(2),
            reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5),
            reader.GetBoolean(6), reader.GetInt64(7), reader.GetDecimal(8));
    }

    private async Task<IReadOnlyList<TransientAccountItem>> GetTransientAccountItemsAsync(
        DashboardMeta meta,
        int limit,
        CancellationToken ct)
    {
        if (!meta.ShiftIsOpen || meta.ShiftNumber is null) return [];
        await using var command = dataSource.CreateCommand("""
            SELECT ts.source_temp_folio, ts.check_number, ts.opened_at, ts.total, ts.tip,
                   ts.paid, ts.payload->>'mesa', ts.payload->>'idMesero', ts.payload->>'usuarioPago'
            FROM transient_sales ts
            WHERE ts.branch_id = $1
              AND ts.source_shift_id = $2
              AND NOT ts.cancelled
              AND NOT ts.paid
              AND NOT EXISTS (
                  SELECT 1 FROM sales s
                  WHERE s.branch_id = ts.branch_id
                    AND s.source_shift_id = ts.source_shift_id
                    AND s.source_temp_folio = ts.source_temp_folio)
            ORDER BY ts.opened_at DESC NULLS LAST, ts.source_temp_folio DESC
            LIMIT $3;
            """);
        command.Parameters.AddWithValue(meta.BranchId);
        command.Parameters.AddWithValue(meta.ShiftNumber.Value);
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var items = new List<TransientAccountItem>();
        while (await reader.ReadAsync(ct))
        {
            items.Add(new TransientAccountItem(
                reader.GetInt64(0),
                ReadNullableString(reader, 1),
                ReadNullableDateTime(reader, 2),
                ReadNullableDecimal(reader, 3),
                ReadNullableDecimal(reader, 4),
                reader.GetBoolean(5),
                ReadNullableString(reader, 6),
                ReadNullableString(reader, 7),
                ReadNullableString(reader, 8)));
        }
        return items;
    }

    private async Task<IReadOnlyList<HourlySalesPoint>> GetHourlySalesAsync(
        DashboardMeta meta,
        CancellationToken ct)
    {
        var start = meta.Date.ToDateTime(TimeOnly.MinValue);
        await using var command = dataSource.CreateCommand("""
            WITH paid_activity AS (
                SELECT closed_at, total
                FROM sales
                WHERE branch_id = $1
                  AND (($4::integer IS NULL AND business_date >= $2 AND business_date < $3)
                       OR ($4::integer IS NOT NULL AND COALESCE(source_shift_id, NULLIF(payload->>'idTurno', '')::integer) = $4))
                  AND paid AND NOT cancelled AND closed_at IS NOT NULL
                UNION ALL
                SELECT ts.closed_at, ts.total
                FROM transient_sales ts
                WHERE ts.branch_id = $1 AND $4::integer IS NOT NULL
                  AND ts.source_shift_id = $4
                  AND ts.paid AND NOT ts.cancelled AND ts.closed_at IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM sales s
                      WHERE s.branch_id = ts.branch_id
                        AND s.source_shift_id = ts.source_shift_id
                        AND s.source_temp_folio = ts.source_temp_folio)
            )
            SELECT EXTRACT(HOUR FROM closed_at)::int AS hour,
                   COALESCE(SUM(total), 0), COUNT(*)
            FROM paid_activity
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
            WITH ticket_activity AS (
                SELECT source_folio, payload->>'numCheque' AS check_number, business_date, closed_at,
                       total, tip, paid, cancelled, payload->>'mesa' AS table_name,
                       payload->>'usuarioPago' AS payment_user, false AS transient
                FROM sales
                WHERE branch_id = $1
                  AND (($4::integer IS NULL AND business_date >= $2 AND business_date < $3)
                       OR ($4::integer IS NOT NULL AND COALESCE(source_shift_id, NULLIF(payload->>'idTurno', '')::integer) = $4))
                  AND ($5::text IS NULL OR source_folio::text ILIKE '%' || $5 || '%'
                       OR COALESCE(payload->>'numCheque', '') ILIKE '%' || $5 || '%')
                UNION ALL
                SELECT ts.source_temp_folio, ts.check_number, ts.opened_at, ts.closed_at,
                       ts.total, ts.tip, ts.paid, ts.cancelled, ts.payload->>'mesa',
                       ts.payload->>'usuarioPago', true AS transient
                FROM transient_sales ts
                WHERE ts.branch_id = $1 AND $4::integer IS NOT NULL
                  AND ts.source_shift_id = $4
                  AND ts.paid AND NOT ts.cancelled AND ts.closed_at IS NOT NULL
                  AND ($5::text IS NULL OR ts.source_temp_folio::text ILIKE '%' || $5 || '%'
                       OR COALESCE(ts.check_number, '') ILIKE '%' || $5 || '%')
                  AND NOT EXISTS (
                      SELECT 1 FROM sales s
                      WHERE s.branch_id = ts.branch_id
                        AND s.source_shift_id = ts.source_shift_id
                        AND s.source_temp_folio = ts.source_temp_folio)
            )
            SELECT * FROM ticket_activity
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

    public async Task<ProductCancellationReport?> GetProductCancellationReportAsync(DashboardUser user, string branchCode, DateOnly from, DateOnly to, int? shiftId, string? userFilter, string? productFilter, int page, int pageSize, CancellationToken ct)
    {
        var meta = await GetMetaAsync(user, branchCode, from, null, ct);
        if (meta is null) return null;
        var start=from.ToDateTime(TimeOnly.MinValue); var end=to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var u=string.IsNullOrWhiteSpace(userFilter)?null:userFilter.Trim(); var p=string.IsNullOrWhiteSpace(productFilter)?null:productFilter.Trim();
        const string f="branch_id=$1 AND cancelled_at >= $2 AND cancelled_at < $3 AND ($4::integer IS NULL OR source_shift_id=$4) AND ($5::text IS NULL OR COALESCE(cancelled_by,'') ILIKE '%' || $5 || '%') AND ($6::text IS NULL OR COALESCE(product_id,'') ILIKE '%' || $6 || '%' OR COALESCE(description,'') ILIKE '%' || $6 || '%')";
        async Task<List<CancellationMetric>> Metric(string label, string order) { await using var c=dataSource.CreateCommand($"SELECT {label},COALESCE(SUM(quantity*unit_price),0),COALESCE(SUM(quantity),0) FROM product_cancellation_events WHERE {f} GROUP BY 1 ORDER BY {order} LIMIT 20;"); AddCancellationFilters(c,meta.BranchId,start,end,shiftId,u,p); await using var r=await c.ExecuteReaderAsync(ct); var x=new List<CancellationMetric>(); while(await r.ReadAsync(ct)) x.Add(new(ReadNullableString(r,0)??"No registrado",r.GetDecimal(1),r.GetDecimal(2))); return x; }
        var employees=Metric("COALESCE(cancelled_by,'No registrado')","2 DESC,3 DESC,1"); var products=Metric("COALESCE(description,product_id,'Sin producto')","2 DESC,3 DESC,1"); var shifts=Metric("COALESCE(source_shift_id::text,'Sin turno')","2 DESC,3 DESC,1"); var days=Metric("to_char(cancelled_at::date,'YYYY-MM-DD')","1 DESC");
        await using var total=dataSource.CreateCommand($"SELECT COALESCE(SUM(quantity*unit_price),0),COALESCE(SUM(quantity),0) FROM product_cancellation_events WHERE {f};"); AddCancellationFilters(total,meta.BranchId,start,end,shiftId,u,p); await using var tr=await total.ExecuteReaderAsync(ct); await tr.ReadAsync(ct);
        await using var cmd=dataSource.CreateCommand($"SELECT event_key,source_kind,cancelled_at,source_folio,source_temp_folio,product_id,description,quantity,unit_price,COALESCE(quantity*unit_price,0),cancelled_by,reason,reason_description,source_shift_id,area_description,company_name,account_opened_at,account_closed_at,account_paid,account_cancelled,account_final_total,source_duplicate_count,account_label,correlation_status,correlation_event_at FROM product_cancellation_events WHERE {f} ORDER BY cancelled_at DESC NULLS LAST,event_key LIMIT $7 OFFSET $8;"); AddCancellationFilters(cmd,meta.BranchId,start,end,shiftId,u,p); cmd.Parameters.AddWithValue(pageSize+1); cmd.Parameters.AddWithValue((page-1)*pageSize);
        await using var r=await cmd.ExecuteReaderAsync(ct); var items=new List<ProductCancellationReportItem>(); while(await r.ReadAsync(ct)){var sourceKind=r.GetString(1);var paid=ReadNullableBool(r,18);var cancelled=ReadNullableBool(r,19);var correlation=ReadNullableString(r,23)??"UNRESOLVED_HISTORICAL";var rawShift=ReadNullableInt64(r,13);items.Add(new(r.GetString(0),sourceKind,ReadNullableDateTime(r,2),ReadNullableInt64(r,3),ReadNullableInt64(r,4),ReadNullableString(r,5),ReadNullableString(r,6),ReadNullableDecimal(r,7),ReadNullableDecimal(r,8),ReadNullableDecimal(r,9),ReadNullableString(r,10),ReadNullableString(r,11),ReadNullableString(r,12),rawShift is { } shiftValue?checked((int?)shiftValue):null,ReadNullableString(r,14),ReadNullableString(r,15),ReadNullableString(r,22),correlation,CancellationAccountStatus(correlation,sourceKind,paid,cancelled),ReadNullableDateTime(r,24),ReadNullableDateTime(r,16),ReadNullableDateTime(r,17),ReadNullableDecimal(r,20),r.GetInt32(21)));}
        var more=items.Count>pageSize;if(more)items.RemoveAt(items.Count-1);await Task.WhenAll(employees,products,shifts,days);return new(meta,tr.GetDecimal(0),tr.GetDecimal(1),await employees,await products,await shifts,await days,items,page,pageSize,more);
    }

    private static void AddCancellationFilters(NpgsqlCommand c,Guid branch,DateTime start,DateTime end,int? shift,string? user,string? product){c.Parameters.AddWithValue(branch);c.Parameters.AddWithValue(start);c.Parameters.AddWithValue(end);c.Parameters.AddWithValue((object?)shift??DBNull.Value);c.Parameters.AddWithValue((object?)user??DBNull.Value);c.Parameters.AddWithValue((object?)product??DBNull.Value);}

    private static string CancellationAccountStatus(string correlationStatus, string sourceKind, bool? paid, bool? cancelled) => correlationStatus switch
    {
        "LINKED_TO_CHECK" when cancelled == true => "Cheque histórico cancelado",
        "LINKED_TO_CHECK" when paid == true => "Cheque histórico cobrado",
        "LINKED_TO_CHECK" => "Cheque histórico",
        "LINKED_TO_ACTIVE_TEMP_ACCOUNT" => "Cuenta temporal activa",
        "ACCOUNT_DELETED_BEFORE_CHECK" => "Cuenta eliminada antes de generar cheque",
        "UNRESOLVED_TRANSIENT" => "Cuenta temporal sin correlación",
        _ when sourceKind == "TRANSIENT" => "Cuenta temporal sin correlación",
        _ => "Cancelación histórica sin cheque asociado"
    };

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
            WHERE branch_id = $1
              AND (($3::integer IS NULL AND cancellation_date = $2::date)
                   OR ($3::integer IS NOT NULL AND EXISTS (SELECT 1 FROM sales s
                       WHERE s.branch_id = cancellation_summaries.branch_id
                         AND s.source_folio = cancellation_summaries.source_folio
                         AND COALESCE(s.source_shift_id, NULLIF(s.payload->>'idTurno', '')::integer) = $3)))
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
              AND (($8::integer IS NULL AND movement_date >= $2 AND movement_date < $3)
                   OR ($8::integer IS NOT NULL AND COALESCE(source_shift_id, NULLIF(payload->>'idTurno', '')::integer) = $8))
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
        ReadNullableString(reader, 9),
        reader.GetBoolean(10));

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
            null, null, null, null, null, null, null, false, null, null,
            null, null, null);

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
