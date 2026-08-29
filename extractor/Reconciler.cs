using SoftRestaurant.Sync.Contracts;

namespace SoftRestaurant.Extractor;

/// <summary>
/// Comparador diario: recalcula los mismos totales de dos maneras independientes
/// (agregando lo ya extraído en memoria vs. una consulta de control directa a
/// `cheques`) y exige que coincidan exactamente. Esto es el "Criterio de salida"
/// de la Fase 1 del plan: "la extracción reproduce venta, tickets, pagos y
/// cancelados sin duplicar".
/// </summary>
internal static class Reconciler
{
    private const decimal Tolerancia = 0.01m; // redondeo, no negociable por encima de esto

    public static ReconciliationResult Compare(
        List<SaleHeader> sales,
        List<SaleLine> lines,
        List<SalePayment> payments,
        ControlTotals control)
    {
        var validSales = sales.Where(s => s.EsVentaValida).ToList();

        var extractedTickets = validSales.Select(s => s.Folio).Distinct().Count();
        var extractedVenta = validSales.Sum(s => s.Total ?? 0m);
        var extractedPropina = validSales.Sum(s => s.Propina ?? 0m);
        var extractedCancelados = sales.Count(s => s.Cancelado);

        var extractedFilasPago = payments.Count;

        var checks = new List<CheckItem>
        {
            new("tickets_validos", extractedTickets, control.TicketsValidos),
            new("venta_valida", extractedVenta, control.VentaValida, Tolerancia),
            new("propina_valida", extractedPropina, control.PropinaValida, Tolerancia),
            new("tickets_cancelados", extractedCancelados, control.TicketsCancelados),
            new("lineas", lines.Count, control.Lineas),
            new("filas_pago", extractedFilasPago, control.FilasPago),
        };

        return new ReconciliationResult(checks.All(c => c.Match), checks);
    }

    public static void PrintReport(ReconciliationResult result)
    {
        foreach (var c in result.Checks)
        {
            var mark = c.Match ? "OK " : "!! ";
            Console.WriteLine($"  {mark}{c.Nombre,-18} extraído={c.Extraido}  control={c.Control}{(c.Match ? "" : "  <-- DIFIERE")}");
        }
    }
}

internal sealed record CheckItem
{
    public string Nombre { get; }
    public decimal Extraido { get; }
    public decimal Control { get; }
    public bool Match { get; }

    public CheckItem(string nombre, decimal extraido, decimal control, decimal tolerancia = 0m)
    {
        Nombre = nombre;
        Extraido = extraido;
        Control = control;
        Match = Math.Abs(extraido - control) <= tolerancia;
    }
}

internal sealed record ReconciliationResult(bool Ok, List<CheckItem> Checks);
