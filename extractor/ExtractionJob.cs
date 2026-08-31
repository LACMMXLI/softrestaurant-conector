using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RestaurantAgent.Sync.Contracts;

namespace RestaurantAgent.Extractor;

internal sealed record ExtractionResult(
    DateTime Desde,
    DateTime Hasta,
    List<SaleHeader> Sales,
    List<SaleLine> Lines,
    List<SalePayment> Payments,
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
        var sales = await extractor.ExtractSalesAsync(ct);
        var lines = await extractor.ExtractSaleLinesAsync(ct);
        var payments = await extractor.ExtractSalePaymentsAsync(ct);
        var shifts = await extractor.ExtractShiftsAsync(ct);
        var declarations = await extractor.ExtractCashierDeclarationsAsync(ct);
        var cashMovements = await extractor.ExtractCashMovementsAsync(ct);
        var cancellations = await extractor.ExtractCancellationsAsync(ct);

        Console.WriteLine($"  ventas={sales.Count}, líneas={lines.Count}, pagos={payments.Count}, turnos={shifts.Count}");
        Console.WriteLine($"  declaraciones={declarations.Count}, caja={cashMovements.Count}, cancelaciones={cancellations.Count}");

        EnsureUnique("ventas", sales.Select(x => x.IdempotencyKey));
        EnsureUnique("líneas", lines.Select(x => x.IdempotencyKey));
        EnsureUnique("pagos", payments.Select(x => x.IdempotencyKey));
        EnsureUnique("turnos", shifts.Select(x => x.IdempotencyKey));
        EnsureUnique("movimientos de caja", cashMovements.Select(x => x.IdempotencyKey));

        var control = await extractor.ExtractControlTotalsAsync(ct);
        var reconciliation = Reconciler.Compare(sales, lines, payments, control);
        Reconciler.PrintReport(reconciliation);

        await WriteJsonAsync("ventas.json", sales, cfg.OutputDirectory, ct);
        await WriteJsonAsync("lineas.json", lines, cfg.OutputDirectory, ct);
        await WriteJsonAsync("pagos.json", payments, cfg.OutputDirectory, ct);
        await WriteJsonAsync("turnos.json", shifts, cfg.OutputDirectory, ct);
        await WriteJsonAsync("declaraciones_cajero.json", declarations, cfg.OutputDirectory, ct);
        await WriteJsonAsync("movimientos_caja.json", cashMovements, cfg.OutputDirectory, ct);
        await WriteJsonAsync("cancelaciones.json", cancellations, cfg.OutputDirectory, ct);
        await WriteJsonAsync("reconciliacion.json", reconciliation, cfg.OutputDirectory, ct);

        return new ExtractionResult(
            desde, hasta, sales, lines, payments, shifts, declarations,
            cashMovements, cancellations, reconciliation);
    }

    public static SyncBatch CreateBatch(ExtractorConfig cfg, ExtractionResult result)
    {
        var cancellationSummaries = result.Cancellations
            .GroupBy(x => new
            {
                Date = (x.Fecha ?? result.Desde).Date,
                x.FolioCheque,
                x.Usuario,
                x.IdProducto,
                x.Descripcion,
                x.Cantidad,
                x.Precio,
                x.Razon
            })
            .Select(group =>
            {
                var value = group.Key;
                var rawKey = string.Join('|',
                    value.Date.ToString("yyyy-MM-dd"), value.FolioCheque, value.Usuario,
                    value.IdProducto, value.Descripcion, value.Cantidad, value.Precio, value.Razon);
                return new CancellationSummary
                {
                    SnapshotKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))).ToLowerInvariant(),
                    Date = value.Date,
                    SourceFolio = value.FolioCheque,
                    User = value.Usuario,
                    ProductId = value.IdProducto,
                    Description = value.Descripcion,
                    Quantity = value.Cantidad,
                    Price = value.Precio,
                    Reason = value.Razon,
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
            Sales = result.Sales,
            Lines = result.Lines,
            Payments = result.Payments,
            Shifts = result.Shifts,
            CashierDeclarations = result.CashierDeclarations,
            CashMovements = result.CashMovements,
            Cancellations = cancellationSummaries,
            Reconciliation = result.Reconciliation.Checks.Select(x => new ReconciliationCheck
            {
                Name = x.Nombre,
                Extracted = x.Extraido,
                Control = x.Control,
                Match = x.Match
            }).ToList()
        };
    }

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
