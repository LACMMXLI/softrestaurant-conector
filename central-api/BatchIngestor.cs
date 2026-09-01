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

        await ExecuteJsonAsync(connection, transaction, branchId, batch.Products, ProductsSql, ct);
        await ExecuteJsonAsync(connection, transaction, branchId, batch.Sales, SalesSql, ct);
        await ExecuteJsonAsync(connection, transaction, branchId, batch.Lines, LinesSql, ct);
        await ExecuteJsonAsync(connection, transaction, branchId, batch.Payments, PaymentsSql, ct);
        if (batch.TransientSnapshotComplete)
            await ApplyTransientSnapshotAsync(connection, transaction, branchId, batch, ct);
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
            products = batch.Products.Count,
            sales = batch.Sales.Count,
            lines = batch.Lines.Count,
            payments = batch.Payments.Count,
            transientSales = batch.TransientSales.Count,
            transientLines = batch.TransientLines.Count,
            transientPayments = batch.TransientPayments.Count,
            transientSnapshotComplete = batch.TransientSnapshotComplete,
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

    private static async Task ApplyTransientSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid branchId,
        SyncBatch batch,
        CancellationToken ct)
    {
        await using (var ensureState = new NpgsqlCommand("""
            INSERT INTO transient_snapshot_state (branch_id, last_created_at, last_batch_id)
            VALUES ($1, $2, $3)
            ON CONFLICT (branch_id) DO NOTHING;
            """, connection, transaction))
        {
            ensureState.Parameters.AddWithValue(branchId);
            ensureState.Parameters.AddWithValue(batch.CreatedAtUtc);
            ensureState.Parameters.AddWithValue(batch.BatchId);
            await ensureState.ExecuteNonQueryAsync(ct);
        }

        DateTime lastCreatedAt;
        await using (var lockState = new NpgsqlCommand("""
            SELECT last_created_at
            FROM transient_snapshot_state
            WHERE branch_id = $1
            FOR UPDATE;
            """, connection, transaction))
        {
            lockState.Parameters.AddWithValue(branchId);
            lastCreatedAt = (DateTime)(await lockState.ExecuteScalarAsync(ct))!;
        }
        if (batch.CreatedAtUtc < lastCreatedAt) return;

        await ExecuteSnapshotJsonAsync(connection, transaction, branchId, batch.BatchId,
            batch.TransientSales, TransientSalesSql, ct);
        await ExecuteSnapshotJsonAsync(connection, transaction, branchId, batch.BatchId,
            batch.TransientLines, TransientLinesSql, ct);
        await ExecuteSnapshotJsonAsync(connection, transaction, branchId, batch.BatchId,
            batch.TransientPayments, TransientPaymentsSql, ct);

        // Cuatro comandos separados a propósito, NO uno solo con las cuatro sentencias unidas por
        // ";": Postgres/Npgsql rechaza combinar múltiples sentencias con parámetros en un mismo
        // comando (protocolo extendido) con "42601: cannot insert multiple commands into a
        // prepared statement" — esto rompía SIEMPRE la primera sincronización (snapshot
        // completo) de cualquier sucursal recién vinculada, ya que este método solo corre cuando
        // batch.TransientSnapshotComplete es true.
        await using (var deleteSales = new NpgsqlCommand("""
            DELETE FROM transient_sales ts
            WHERE ts.branch_id = $1
              AND (ts.snapshot_id <> $2 OR EXISTS (
                  SELECT 1 FROM sales s
                  WHERE s.branch_id = ts.branch_id
                    AND s.source_shift_id = ts.source_shift_id
                    AND s.source_temp_folio = ts.source_temp_folio
                    AND s.source_temp_folio IS NOT NULL));
            """, connection, transaction))
        {
            deleteSales.Parameters.AddWithValue(branchId);
            deleteSales.Parameters.AddWithValue(batch.BatchId);
            await deleteSales.ExecuteNonQueryAsync(ct);
        }

        await using (var deleteLines = new NpgsqlCommand("""
            DELETE FROM transient_sale_lines tl
            WHERE tl.branch_id = $1
              AND (tl.snapshot_id <> $2 OR NOT EXISTS (
                  SELECT 1 FROM transient_sales ts
                  WHERE ts.branch_id = tl.branch_id AND ts.idempotency_key = tl.header_key));
            """, connection, transaction))
        {
            deleteLines.Parameters.AddWithValue(branchId);
            deleteLines.Parameters.AddWithValue(batch.BatchId);
            await deleteLines.ExecuteNonQueryAsync(ct);
        }

        await using (var deletePayments = new NpgsqlCommand("""
            DELETE FROM transient_sale_payments tp
            WHERE tp.branch_id = $1
              AND (tp.snapshot_id <> $2 OR NOT EXISTS (
                  SELECT 1 FROM transient_sales ts
                  WHERE ts.branch_id = tp.branch_id AND ts.idempotency_key = tp.header_key));
            """, connection, transaction))
        {
            deletePayments.Parameters.AddWithValue(branchId);
            deletePayments.Parameters.AddWithValue(batch.BatchId);
            await deletePayments.ExecuteNonQueryAsync(ct);
        }

        await using (var updateState = new NpgsqlCommand("""
            UPDATE transient_snapshot_state
            SET last_created_at = $3, last_batch_id = $2, updated_at = now()
            WHERE branch_id = $1;
            """, connection, transaction))
        {
            updateState.Parameters.AddWithValue(branchId);
            updateState.Parameters.AddWithValue(batch.BatchId);
            updateState.Parameters.AddWithValue(batch.CreatedAtUtc);
            await updateState.ExecuteNonQueryAsync(ct);
        }
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

    private static async Task ExecuteSnapshotJsonAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid branchId,
        string snapshotId,
        List<T> values,
        string sql,
        CancellationToken ct)
    {
        if (values.Count == 0) return;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(branchId);
        command.Parameters.AddWithValue(JsonSerializer.Serialize(values, JsonOptions));
        command.Parameters.AddWithValue(snapshotId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private const string SalesSql = """
        INSERT INTO sales
            (branch_id, idempotency_key, source_folio, source_shift_id, source_temp_folio, business_date, closed_at,
             paid, cancelled, total, tip, payload)
        SELECT $1,
               item->>'idempotencyKey',
               (item->>'folio')::bigint,
               NULLIF(item->>'idTurno', '')::integer,
               NULLIF(item->>'foliotTempCheques', '')::bigint,
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
            source_temp_folio = excluded.source_temp_folio,
            business_date = excluded.business_date,
            closed_at = excluded.closed_at,
            paid = excluded.paid,
            cancelled = excluded.cancelled,
            total = excluded.total,
            tip = excluded.tip,
            payload = excluded.payload,
            updated_at = now();
        """;

    private const string ProductsSql = """
        INSERT INTO products
            (branch_id, product_id, description, group_id, group_name, classification, active, payload)
        SELECT $1,
               item->>'idProducto',
               NULLIF(item->>'descripcion', ''),
               NULLIF(item->>'idGrupo', ''),
               NULLIF(item->>'grupo', ''),
               NULLIF(item->>'clasificacion', '')::integer,
               COALESCE((item->>'activo')::boolean, true),
               item
        FROM jsonb_array_elements($2::jsonb) AS item
        ON CONFLICT (branch_id, product_id) DO UPDATE
        SET description = excluded.description,
            group_id = excluded.group_id,
            group_name = excluded.group_name,
            classification = excluded.classification,
            active = excluded.active,
            payload = excluded.payload,
            updated_at = now();
        """;

    private const string TransientSalesSql = """
        INSERT INTO transient_sales
            (branch_id, idempotency_key, source_temp_folio, source_shift_id, check_number,
             opened_at, closed_at, paid, cancelled, total, tip, payload, snapshot_id)
        SELECT $1,
               item->>'idempotencyKey',
               (item->>'tempFolio')::bigint,
               NULLIF(item->>'idTurno', '')::integer,
               NULLIF(item->>'numCheque', ''),
               NULLIF(item->>'fecha', '')::timestamp,
               NULLIF(item->>'cierre', '')::timestamp,
               COALESCE((item->>'pagado')::boolean, false),
               COALESCE((item->>'cancelado')::boolean, false),
               NULLIF(item->>'total', '')::numeric,
               NULLIF(item->>'propina', '')::numeric,
               item,
               $3
        FROM jsonb_array_elements($2::jsonb) AS item
        ON CONFLICT (branch_id, idempotency_key) DO UPDATE
        SET source_temp_folio = excluded.source_temp_folio,
            source_shift_id = excluded.source_shift_id,
            check_number = excluded.check_number,
            opened_at = excluded.opened_at,
            closed_at = excluded.closed_at,
            paid = excluded.paid,
            cancelled = excluded.cancelled,
            total = excluded.total,
            tip = excluded.tip,
            payload = excluded.payload,
            snapshot_id = excluded.snapshot_id,
            updated_at = now();
        """;

    private const string TransientLinesSql = """
        INSERT INTO transient_sale_lines
            (branch_id, idempotency_key, header_key, source_temp_folio, source_shift_id,
             product_id, quantity, price, payload, snapshot_id)
        SELECT $1,
               item->>'idempotencyKey',
               item->>'headerKey',
               (item->>'tempFolio')::bigint,
               NULLIF(item->>'idTurno', '')::integer,
               item->>'idProducto',
               NULLIF(item->>'cantidad', '')::numeric,
               NULLIF(item->>'precio', '')::numeric,
               item,
               $3
        FROM jsonb_array_elements($2::jsonb) AS item
        ON CONFLICT (branch_id, idempotency_key) DO UPDATE
        SET header_key = excluded.header_key,
            source_temp_folio = excluded.source_temp_folio,
            source_shift_id = excluded.source_shift_id,
            product_id = excluded.product_id,
            quantity = excluded.quantity,
            price = excluded.price,
            payload = excluded.payload,
            snapshot_id = excluded.snapshot_id,
            updated_at = now();
        """;

    private const string TransientPaymentsSql = """
        INSERT INTO transient_sale_payments
            (branch_id, idempotency_key, header_key, source_temp_folio, source_shift_id,
             payment_method, amount, tip, exchange_rate, payload, snapshot_id)
        SELECT $1,
               item->>'idempotencyKey',
               item->>'headerKey',
               (item->>'tempFolio')::bigint,
               NULLIF(item->>'idTurno', '')::integer,
               item->>'idFormaDePago',
               NULLIF(item->>'importe', '')::numeric,
               NULLIF(item->>'propina', '')::numeric,
               NULLIF(item->>'tipoDeCambio', '')::numeric,
               item,
               $3
        FROM jsonb_array_elements($2::jsonb) AS item
        ON CONFLICT (branch_id, idempotency_key) DO UPDATE
        SET header_key = excluded.header_key,
            source_temp_folio = excluded.source_temp_folio,
            source_shift_id = excluded.source_shift_id,
            payment_method = excluded.payment_method,
            amount = excluded.amount,
            tip = excluded.tip,
            exchange_rate = excluded.exchange_rate,
            payload = excluded.payload,
            snapshot_id = excluded.snapshot_id,
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
