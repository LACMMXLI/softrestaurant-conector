using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RestaurantAgent.Sync.Contracts;

namespace RestaurantAgent.Extractor;

internal sealed record ExtractionResult(
    DateTime Desde,
    DateTime Hasta,
    List<ProductCatalogItem> Products,
    List<SaleHeader> Sales,
    List<SaleLine> Lines,
    List<SalePayment> Payments,
    List<TransientSaleHeader> TransientSales,
    List<TransientSaleLine> TransientLines,
    List<TransientSalePayment> TransientPayments,
    List<Shift> Shifts,
    List<CashierDeclaration> CashierDeclarations,
    List<CashMovement> CashMovements,
    List<CancelledLine> Cancellations,
    ReconciliationResult Reconciliation);

internal static class ExtractionJob
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<ExtractionResult> RunAsync(ExtractorConfig cfg, CancellationToken ct)
    {
        var (desde, hasta) = cfg.GetRunRange();
        var extractor = new Extractor(cfg.BuildConnectionString(), desde, hasta);
        Directory.CreateDirectory(cfg.OutputDirectory);

        Console.WriteLine($"Extrayendo rango [{desde:yyyy-MM-dd}, {hasta:yyyy-MM-dd})...");
        var products = await extractor.ExtractProductsAsync(ct);
        var sales = await extractor.ExtractSalesAsync(ct);
        var lines = await extractor.ExtractSaleLinesAsync(ct);
        var payments = await extractor.ExtractSalePaymentsAsync(ct);
        var transientSales = await extractor.ExtractTransientSalesAsync(ct);
        var transientLines = await extractor.ExtractTransientSaleLinesAsync(ct);
        var transientPayments = await extractor.ExtractTransientSalePaymentsAsync(ct);
        var shifts = await extractor.ExtractShiftsAsync(ct);
        var declarations = await extractor.ExtractCashierDeclarationsAsync(ct);
        var cashMovements = await extractor.ExtractCashMovementsAsync(ct);
        var cancellations = await extractor.ExtractCancellationsAsync(ct);

        Console.WriteLine($"  productos={products.Count}, ventas={sales.Count}, líneas={lines.Count}, pagos={payments.Count}, turnos={shifts.Count}");
        Console.WriteLine($"  transitorias={transientSales.Count}, líneas transitorias={transientLines.Count}, pagos transitorios={transientPayments.Count}");
        Console.WriteLine($"  declaraciones={declarations.Count}, caja={cashMovements.Count}, cancelaciones={cancellations.Count}");

        EnsureUnique("productos", products.Select(x => x.IdProducto));
        EnsureUnique("ventas", sales.Select(x => x.IdempotencyKey));
        EnsureUnique("líneas", lines.Select(x => x.IdempotencyKey));
        EnsureUnique("pagos", payments.Select(x => x.IdempotencyKey));
        EnsureUnique("cuentas transitorias", transientSales.Select(x => x.IdempotencyKey));
        EnsureUnique("líneas transitorias", transientLines.Select(x => x.IdempotencyKey));
        EnsureUnique("pagos transitorios", transientPayments.Select(x => x.IdempotencyKey));
        EnsureUnique("turnos", shifts.Select(x => x.IdempotencyKey));
        EnsureUnique("movimientos de caja", cashMovements.Select(x => x.IdempotencyKey));

        var control = await extractor.ExtractControlTotalsAsync(ct);
        var reconciliation = Reconciler.Compare(sales, lines, payments, control);
        Reconciler.PrintReport(reconciliation);

        await WriteJsonAsync("productos.json", products, cfg.OutputDirectory, ct);
        await WriteJsonAsync("ventas.json", sales, cfg.OutputDirectory, ct);
        await WriteJsonAsync("lineas.json", lines, cfg.OutputDirectory, ct);
        await WriteJsonAsync("pagos.json", payments, cfg.OutputDirectory, ct);
        await WriteJsonAsync("cuentas_transitorias.json", transientSales, cfg.OutputDirectory, ct);
        await WriteJsonAsync("lineas_transitorias.json", transientLines, cfg.OutputDirectory, ct);
        await WriteJsonAsync("pagos_transitorios.json", transientPayments, cfg.OutputDirectory, ct);
        await WriteJsonAsync("turnos.json", shifts, cfg.OutputDirectory, ct);
        await WriteJsonAsync("declaraciones_cajero.json", declarations, cfg.OutputDirectory, ct);
        await WriteJsonAsync("movimientos_caja.json", cashMovements, cfg.OutputDirectory, ct);
        await WriteJsonAsync("cancelaciones.json", cancellations, cfg.OutputDirectory, ct);
        await WriteJsonAsync("reconciliacion.json", reconciliation, cfg.OutputDirectory, ct);

        return new ExtractionResult(
            desde, hasta, products, sales, lines, payments, transientSales, transientLines, transientPayments, shifts, declarations,
            cashMovements, cancellations, reconciliation);
    }

    public static SyncBatch CreateBatch(ExtractorConfig cfg, ExtractionResult result)
    {
        // `cancela` no tiene PK. La llave es una huella del evento disponible en ambas tablas:
        // cuando tempcancela se consolida, cheques.foliotempcheques conserva el ancla temporal.
        // saledetailid se conserva para trazabilidad, pero no forma la llave porque cancela no lo
        // guarda. Filas idénticas de origen se colapsan y se reporta SourceDuplicateCount.
        var cancellationEvents = result.Cancellations
            .GroupBy(CancellationEventKey)
            .Select(group => ToCancellationEvent(group.Key, group.OrderByDescending(x => x.SourceKind == "HISTORICAL").First(), group.Count()))
            .OrderBy(x => x.CancelledAt)
            .ThenBy(x => x.EventKey, StringComparer.Ordinal)
            .ToList();

        var cancellationSummaries = cancellationEvents
            .GroupBy(x => new
            {
                Date = (x.CancelledAt ?? result.Desde).Date,
                FolioCheque = x.SourceFolio ?? x.SourceTempFolio,
                x.User, x.ProductId, x.Description, Quantity = x.Quantity, Price = x.UnitPrice, Reason = x.Reason
            })
            .Select(group =>
            {
                var value = group.Key;
                var rawKey = string.Join('|',
                    value.Date.ToString("yyyy-MM-dd"), value.FolioCheque, value.User,
                    value.ProductId, value.Description, value.Quantity, value.Price, value.Reason);
                return new CancellationSummary
                {
                    SnapshotKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))).ToLowerInvariant(),
                    Date = value.Date,
                    SourceFolio = value.FolioCheque,
                    User = value.User,
                    ProductId = value.ProductId,
                    Description = value.Description,
                    Quantity = value.Quantity,
                    Price = value.Price,
                    Reason = value.Reason,
                    Occurrences = group.Count()
                };
            })
            .OrderBy(x => x.Date)
            .ThenBy(x => x.SnapshotKey, StringComparer.Ordinal)
            .ToList();

        return new SyncBatch
        {
            BatchId = Guid.NewGuid().ToString("N"),
            BranchCode = cfg.BranchCode,
            RangeStart = result.Desde,
            RangeEnd = result.Hasta,
            CreatedAtUtc = DateTime.UtcNow,
            AgentVersion = typeof(ExtractionJob).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            ReconciliationOk = result.Reconciliation.Ok,
            Products = result.Products,
            Sales = result.Sales,
            Lines = result.Lines,
            Payments = result.Payments,
            TransientSnapshotComplete = true,
            TransientSales = result.TransientSales,
            TransientLines = result.TransientLines,
            TransientPayments = result.TransientPayments,
            Shifts = result.Shifts,
            CashierDeclarations = result.CashierDeclarations,
            CashMovements = result.CashMovements,
            Cancellations = cancellationSummaries,
            TransientCancellationsSnapshotComplete = true,
            ProductCancellations = cancellationEvents,
            Reconciliation = result.Reconciliation.Checks.Select(x => new ReconciliationCheck
            {
                Name = x.Nombre,
                Extracted = x.Extraido,
                Control = x.Control,
                Match = x.Match
            }).ToList()
        };
    }

    private static string CancellationEventKey(CancelledLine value)
    {
        var accountAnchor = value.FolioTemporal is > 0 ? $"temp:{value.FolioTemporal}" : $"folio:{value.FolioCheque}";
        var raw = string.Join('|', accountAnchor, Normalize(value.Comanda), Normalize(value.IdProducto),
            value.Cantidad?.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            value.Precio?.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            value.Fecha?.ToString("yyyy-MM-ddTHH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture),
            Normalize(value.Usuario), Normalize(value.Razon), Normalize(value.IdMotivoCancela));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static ProductCancellationEvent ToCancellationEvent(string eventKey, CancelledLine value, int duplicateCount) => new()
    {
        EventKey = eventKey, SourceKind = value.SourceKind, CancelledAt = value.Fecha,
        SourceFolio = value.FolioCheque, SourceTempFolio = value.FolioTemporal, SaleDetailId = value.SaleDetailId,
        Comanda = value.Comanda, ProductId = value.IdProducto, Description = value.Descripcion,
        Quantity = value.Cantidad, UnitPrice = value.Precio, User = value.Usuario, Reason = value.Razon,
        ReasonId = value.IdMotivoCancela, ReasonDescription = value.MotivoDescripcion, ShiftId = value.IdTurno,
        AreaId = value.IdAreaRestaurant, AreaDescription = value.AreaDescripcion, CompanyId = value.IdEmpresa,
        CompanyName = value.EmpresaNombre, AccountOpenedAt = value.CuentaAbiertaEn, AccountClosedAt = value.CuentaCerradaEn,
        AccountPaid = value.CuentaPagada, AccountCancelled = value.CuentaCancelada, AccountFinalTotal = value.TotalFinalCuenta,
        AccountLabel = value.CuentaReferencia, CorrelationStatus = value.EstadoCorrelacion,
        CorrelationEventAt = value.EventoCorrelacionEn,
        SourceDuplicateCount = duplicateCount
    };

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static async Task WriteJsonAsync<T>(string fileName, T value, string outputDirectory, CancellationToken ct)
    {
        var path = Path.Combine(outputDirectory, fileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, ct);
    }

    private static void EnsureUnique(string label, IEnumerable<string> keys)
    {
        var values = keys.ToList();
        var duplicates = values.Count - values.Distinct(StringComparer.Ordinal).Count();
        if (duplicates > 0)
        {
            throw new InvalidDataException($"{label}: se encontraron {duplicates} llaves idempotentes duplicadas.");
        }

        Console.WriteLine($"  OK {label}: {values.Count} llaves, 0 duplicadas");
    }
}
