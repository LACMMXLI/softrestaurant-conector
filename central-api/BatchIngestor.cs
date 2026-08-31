using System.Text.Json;
using Npgsql;
using RestaurantAgent.Sync.Contracts;

namespace RestaurantAgent.CentralApi;

internal sealed class BatchIngestor(NpgsqlDataSource dataSource)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task IngestAsync(Guid branchId, Guid connectorInstallationId, SyncBatch batch, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await ExecuteJsonAsync(connection, transaction, branchId, batch.Sales, SalesSql, ct);
        await ExecuteJsonAsync(connection, transaction, branchId, batch.Lines, LinesSql, ct);
        await ExecuteJsonAsync(connection, transaction, branchId, batch.Payments, PaymentsSql, ct);
        await ExecuteJsonAsync(connection, transaction, branchId, batch.Shifts, ShiftsSql, ct);
        await ExecuteJsonAsync(connection, transaction, branchId, batch.CashierDeclarations, DeclarationsSql, ct);
        await ExecuteJsonAsync(connection, transaction, branchId, batch.CashMovements, CashMovementsSql, ct);

        await using (var delete = new NpgsqlCommand("""
            DELETE FROM cancellation_summaries
            WHERE branch_id = $1
              AND cancellation_date >= $2::date
              AND cancellation_date < $3::date;
            """, connection, transaction))
        {
            delete.Parameters.AddWithValue(branchId);
            delete.Parameters.AddWithValue(batch.RangeStart);
            delete.Parameters.AddWithValue(batch.RangeEnd);
            await delete.ExecuteNonQueryAsync(ct);
        }
        await ExecuteJsonAsync(connection, transaction, branchId, batch.Cancellations, CancellationsSql, ct);

        var counts = JsonSerializer.Serialize(new
        {
            sales = batch.Sales.Count,
            lines = batch.Lines.Count,
            payments = batch.Payments.Count,
            shifts = batch.Shifts.Count,
            cashDeclarations = batch.CashierDeclarations.Count,
            cashMovements = batch.CashMovements.Count,
            cancellations = batch.Cancellations.Count
        }, JsonOptions);

        await using (var sync = new NpgsqlCommand("""
            INSERT INTO sync_batches
                (id, branch_id, range_start, range_end, agent_version, reconciliation_ok, counts)
            VALUES ($1, $2, $3, $4, $5, $6, $7::jsonb)
            ON CONFLICT (branch_id, id) DO UPDATE
            SET range_start = excluded.range_start,
                range_end = excluded.range_end,
                agent_version = excluded.agent_version,
                reconciliation_ok = excluded.reconciliation_ok,
                counts = excluded.counts,
                received_at = now();
            """, connection, transaction))
        {
            sync.Parameters.AddWithValue(batch.BatchId);
            sync.Parameters.AddWithValue(branchId);
            sync.Parameters.AddWithValue(batch.RangeStart);
            sync.Parameters.AddWithValue(batch.RangeEnd);
            sync.Parameters.AddWithValue(batch.AgentVersion);
            sync.Parameters.AddWithValue(batch.ReconciliationOk);
            sync.Parameters.AddWithValue(counts);
            await sync.ExecuteNonQueryAsync(ct);
        }

        await using (var heartbeat = new NpgsqlCommand("""
            UPDATE branches
            SET last_sync_at = now(), updated_at = now()
            WHERE id = $1;
            """, connection, transaction))
        {
            heartbeat.Parameters.AddWithValue(branchId);
            await heartbeat.ExecuteNonQueryAsync(ct);
        }

        await ConnectorInstallationRegistry.RecordSuccessAsync(connection, transaction, connectorInstallationId, ct);

        await transaction.CommitAsync(ct);
    }

    private static async Task ExecuteJsonAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid branchId,
        List<T> values,
        string sql,
        CancellationToken ct)
    {
        if (values.Count == 0) return;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(branchId);
        command.Parameters.AddWithValue(JsonSerializer.Serialize(values, JsonOptions));
        await command.ExecuteNonQueryAsync(ct);
    }

    private const string SalesSql = """
        INSERT INTO sales
            (branch_id, idempotency_key, source_folio, source_shift_id, business_date, closed_at,
             paid, cancelled, total, tip, payload)
        SELECT $1,
               item->>'idempotencyKey',
               (item->>'folio')::bigint,
               NULLIF(item->>'idTurnoInterno', '')::integer,
               NULLIF(item->>'fecha', '')::timestamp,
               NULLIF(item->>'cierre', '')::timestamp,
               COALESCE((item->>'pagado')::boolean, false),
               COALESCE((item->>'cancelado')::boolean, false),
               NULLIF(item->>'total', '')::numeric,
               NULLIF(item->>'propina', '')::numeric,
               item
        FROM jsonb_array_elements($2::jsonb) AS item
        ON CONFLICT (branch_id, idempotency_key) DO UPDATE
        SET source_folio = excluded.source_folio,
            source_shift_id = excluded.source_shift_id,
            business_date = excluded.business_date,
            closed_at = excluded.closed_at,
            paid = excluded.paid,
            cancelled = excluded.cancelled,
            total = excluded.total,
            tip = excluded.tip,
            payload = excluded.payload,
            updated_at = now();
        """;

    private const string LinesSql = """
        INSERT INTO sale_lines
            (branch_id, idempotency_key, source_folio, product_id, quantity, price, payload)
        SELECT $1,
               item->>'idempotencyKey',
               (item->>'folioDet')::bigint,
               item->>'idProducto',
               NULLIF(item->>'cantidad', '')::numeric,
               NULLIF(item->>'precio', '')::numeric,
               item
        FROM jsonb_array_elements($2::jsonb) AS item
        ON CONFLICT (branch_id, idempotency_key) DO UPDATE
        SET source_folio = excluded.source_folio,
            product_id = excluded.product_id,
            quantity = excluded.quantity,
            price = excluded.price,
            payload = excluded.payload,
            updated_at = now();
        """;

    private const string PaymentsSql = """
        INSERT INTO sale_payments
            (branch_id, idempotency_key, source_folio, payment_method, amount, tip, exchange_rate, payload)
        SELECT $1,
               item->>'idempotencyKey',
               (item->>'folio')::bigint,
               item->>'idFormaDePago',
               NULLIF(item->>'importe', '')::numeric,
               NULLIF(item->>'propina', '')::numeric,
               NULLIF(item->>'tipoDeCambio', '')::numeric,
               item
        FROM jsonb_array_elements($2::jsonb) AS item
        ON CONFLICT (branch_id, idempotency_key) DO UPDATE
        SET source_folio = excluded.source_folio,
            payment_method = excluded.payment_method,
            amount = excluded.amount,
            tip = excluded.tip,
            exchange_rate = excluded.exchange_rate,
            payload = excluded.payload,
            updated_at = now();
        """;

    private const string ShiftsSql = """
        INSERT INTO shifts
            (branch_id, idempotency_key, source_shift_id, opened_at, closed_at, payload)
        SELECT $1,
               item->>'idempotencyKey',
               (item->>'idTurnoInterno')::integer,
               NULLIF(item->>'apertura', '')::timestamp,
               NULLIF(item->>'cierre', '')::timestamp,
               item
        FROM jsonb_array_elements($2::jsonb) AS item
        ON CONFLICT (branch_id, idempotency_key) DO UPDATE
        SET source_shift_id = excluded.source_shift_id,
            opened_at = excluded.opened_at,
            closed_at = excluded.closed_at,
            payload = excluded.payload,
            updated_at = now();
        """;

    private const string DeclarationsSql = """
        INSERT INTO cash_declarations
            (branch_id, idempotency_key, source_shift_id, payment_method, amount, payload)
        SELECT $1,
               item->>'idempotencyKey',
               (item->>'idTurnoInterno')::integer,
               item->>'idFormaDePago',
               NULLIF(item->>'importeDeclarado', '')::numeric,
               item
        FROM jsonb_array_elements($2::jsonb) AS item
        ON CONFLICT (branch_id, idempotency_key) DO UPDATE
        SET source_shift_id = excluded.source_shift_id,
            payment_method = excluded.payment_method,
            amount = excluded.amount,
            payload = excluded.payload,
            updated_at = now();
        """;

    private const string CashMovementsSql = """
        INSERT INTO cash_movements
            (branch_id, idempotency_key, source_folio, source_shift_id, movement_date, movement_type,
             amount, cancelled, payload)
        SELECT $1,
               item->>'idempotencyKey',
               (item->>'folio')::bigint,
               NULLIF(item->>'idTurno', '')::integer,
               NULLIF(item->>'fecha', '')::timestamp,
               (item->>'tipo')::integer,
               NULLIF(item->>'importe', '')::numeric,
               COALESCE((item->>'cancelado')::boolean, false),
               item
        FROM jsonb_array_elements($2::jsonb) AS item
        ON CONFLICT (branch_id, idempotency_key) DO UPDATE
        SET source_folio = excluded.source_folio,
            source_shift_id = excluded.source_shift_id,
            movement_date = excluded.movement_date,
            movement_type = excluded.movement_type,
            amount = excluded.amount,
            cancelled = excluded.cancelled,
            payload = excluded.payload,
            updated_at = now();
        """;

    private const string CancellationsSql = """
        INSERT INTO cancellation_summaries
            (branch_id, snapshot_key, cancellation_date, source_folio, product_id,
             quantity, price, occurrences, payload)
        SELECT $1,
               item->>'snapshotKey',
               (item->>'date')::date,
               NULLIF(item->>'sourceFolio', '')::bigint,
               item->>'productId',
               NULLIF(item->>'quantity', '')::numeric,
               NULLIF(item->>'price', '')::numeric,
               (item->>'occurrences')::integer,
               item
        FROM jsonb_array_elements($2::jsonb) AS item
        ON CONFLICT (branch_id, snapshot_key) DO UPDATE
        SET cancellation_date = excluded.cancellation_date,
            source_folio = excluded.source_folio,
            product_id = excluded.product_id,
            quantity = excluded.quantity,
            price = excluded.price,
            occurrences = excluded.occurrences,
            payload = excluded.payload,
            updated_at = now();
        """;
}
