namespace RestaurantAgent.Sync.Contracts;

public sealed record SaleHeader
{
    public string? WorkspaceId { get; init; }
    public long Folio { get; init; }
    public string? NumCheque { get; init; }
    public string? IdEmpresa { get; init; }
    public DateTime? Fecha { get; init; }
    public DateTime? Cierre { get; init; }
    public DateTime? FechaCancelado { get; init; }
    public bool Pagado { get; init; }
    public bool Cancelado { get; init; }
    public bool Facturado { get; init; }
    public int? IdTurno { get; init; }
    public string? Estacion { get; init; }
    public string? IdAreaRestaurant { get; init; }
    public string? Mesa { get; init; }
    public string? IdMesero { get; init; }
    public string? UsuarioPago { get; init; }
    public string? FoliotTempCheques { get; init; }
    public decimal? Subtotal { get; init; }
    public decimal? SubtotalSinImpuestos { get; init; }
    public decimal? Descuento { get; init; }
    public decimal? DescuentoImporte { get; init; }
    public decimal? TotalImpuesto1 { get; init; }
    public decimal? TotalImpuestoD1 { get; init; }
    public decimal? TotalImpuestoD2 { get; init; }
    public decimal? TotalImpuestoD3 { get; init; }
    public decimal? Total { get; init; }
    public decimal? Propina { get; init; }
    public decimal? PropinaTarjeta { get; init; }
    public decimal? Cargo { get; init; }
    public decimal? Donativo { get; init; }
    public decimal? Cambio { get; init; }
    public string? UsuarioCancelo { get; init; }
    public string? RazonCancelado { get; init; }
    public string? IdMotivoCancela { get; init; }

    public bool EsVentaValida => Pagado && !Cancelado && Cierre is not null;
    public string IdempotencyKey => !string.IsNullOrWhiteSpace(WorkspaceId)
        ? WorkspaceId!
        : $"{IdEmpresa}:{Folio}";
}

/// <summary>
/// Snapshot de una cuenta que todavía vive en dbo.tempcheques. No representa una
/// venta definitiva: puede cambiar, cancelarse o desaparecer cuando SoftRestaurant
/// la traslada a dbo.cheques.
/// </summary>
public sealed record TransientSaleHeader
{
    public string? WorkspaceId { get; init; }
    public long TempFolio { get; init; }
    public string? NumCheque { get; init; }
    public DateTime? Fecha { get; init; }
    public DateTime? Cierre { get; init; }
    public bool Pagado { get; init; }
    public bool Cancelado { get; init; }
    public int? IdTurno { get; init; }
    public bool CuentaEnUso { get; init; }
    public bool? CuentaPagadaProcesada { get; init; }
    public string? Estacion { get; init; }
    public string? Mesa { get; init; }
    public string? IdMesero { get; init; }
    public string? UsuarioPago { get; init; }
    public decimal? Subtotal { get; init; }
    public decimal? Total { get; init; }
    public decimal? Propina { get; init; }

    public string IdempotencyKey => IdTurno is > 0
        ? $"{IdTurno}:{TempFolio}"
        : !string.IsNullOrWhiteSpace(WorkspaceId)
            ? $"sin-turno:{WorkspaceId}"
            : $"sin-turno:{TempFolio}";
}

public sealed record TransientSaleLine
{
    public string? WorkspaceId { get; init; }
    public string? HeaderWorkspaceId { get; init; }
    public long TempFolio { get; init; }
    public int? IdTurno { get; init; }
    public int Movimiento { get; init; }
    public int? Comanda { get; init; }
    public string? IdProducto { get; init; }
    public string? DescripcionProducto { get; init; }
    public decimal? Cantidad { get; init; }
    public decimal? Precio { get; init; }
    public decimal? Descuento { get; init; }
    public DateTime? Hora { get; init; }
    public string? Comentario { get; init; }

    public string HeaderKey => IdTurno is > 0
        ? $"{IdTurno}:{TempFolio}"
        : !string.IsNullOrWhiteSpace(HeaderWorkspaceId)
            ? $"sin-turno:{HeaderWorkspaceId}"
            : $"sin-turno:{TempFolio}";
    public string IdempotencyKey => !string.IsNullOrWhiteSpace(WorkspaceId)
        ? WorkspaceId!
        : $"{HeaderKey}:{Movimiento}:{Comanda}";
}

public sealed record TransientSalePayment
{
    public string? WorkspaceId { get; init; }
    public string? HeaderWorkspaceId { get; init; }
    public long TempFolio { get; init; }
    public int? IdTurno { get; init; }
    public string? IdFormaDePago { get; init; }
    public string? DescripcionFormaDePago { get; init; }
    public int? TipoFormaDePago { get; init; }
    public decimal? Importe { get; init; }
    public decimal? Propina { get; init; }
    public decimal? TipoDeCambio { get; init; }
    public string? Referencia { get; init; }
    public string? CardBrand { get; init; }

    public string HeaderKey => IdTurno is > 0
        ? $"{IdTurno}:{TempFolio}"
        : !string.IsNullOrWhiteSpace(HeaderWorkspaceId)
            ? $"sin-turno:{HeaderWorkspaceId}"
            : $"sin-turno:{TempFolio}";
    public string IdempotencyKey => !string.IsNullOrWhiteSpace(WorkspaceId)
        ? WorkspaceId!
        : $"{HeaderKey}:{IdFormaDePago}:{Importe}:{Referencia}";
}

public sealed record SaleLine
{
    public string? WorkspaceId { get; init; }
    public long FolioDet { get; init; }
    public int Movimiento { get; init; }
    public int? Comanda { get; init; }
    public string? IdProducto { get; init; }
    public string? DescripcionProducto { get; init; }
    public decimal? Cantidad { get; init; }
    public decimal? Precio { get; init; }
    public decimal? PrecioSinImpuestos { get; init; }
    public decimal? PrecioCatalogo { get; init; }
    public decimal? Descuento { get; init; }
    public DateTime? Hora { get; init; }
    public string? Comentario { get; init; }
    public string? IdMeseroProducto { get; init; }
    public string? Modificador { get; init; }

    public string IdempotencyKey => !string.IsNullOrWhiteSpace(WorkspaceId)
        ? WorkspaceId!
        : $"{FolioDet}:{Movimiento}:{Comanda}";
}

public sealed record SalePayment
{
    public string? WorkspaceId { get; init; }
    public long Folio { get; init; }
    public string? IdFormaDePago { get; init; }
    public string? DescripcionFormaDePago { get; init; }
    public int? TipoFormaDePago { get; init; }
    public decimal? Importe { get; init; }
    public decimal? Propina { get; init; }
    public decimal? TipoDeCambio { get; init; }
    public string? Referencia { get; init; }
    public string? CardBrand { get; init; }
    public int? IdTurnoCierre { get; init; }

    public string IdempotencyKey => !string.IsNullOrWhiteSpace(WorkspaceId)
        ? WorkspaceId!
        : $"{Folio}:{IdFormaDePago}:{Importe}:{Referencia}";
}

public sealed record Shift
{
    public string? WorkspaceId { get; init; }
    public int IdTurnoInterno { get; init; }
    public int IdTurno { get; init; }
    public string? IdEstacion { get; init; }
    public DateTime? Apertura { get; init; }
    public DateTime? Cierre { get; init; }
    public string? Cajero { get; init; }
    public decimal? Fondo { get; init; }
    public decimal? Efectivo { get; init; }
    public decimal? Tarjeta { get; init; }
    public decimal? Vales { get; init; }
    public decimal? Credito { get; init; }
    public bool? Procesado { get; init; }

    public string IdempotencyKey => !string.IsNullOrWhiteSpace(WorkspaceId)
        ? WorkspaceId!
        : IdTurnoInterno.ToString();
}

public sealed record CashierDeclaration
{
    public int IdTurnoInterno { get; init; }
    public string? IdFormaDePago { get; init; }
    public int? Tipo { get; init; }
    public decimal? ImporteDeclarado { get; init; }
    public decimal? TipoDeCambio { get; init; }

    public string IdempotencyKey => $"{IdTurnoInterno}:{IdFormaDePago}:{Tipo}";
}

public sealed record CashMovement
{
    public long Folio { get; init; }
    public int? IdTurno { get; init; }
    public int Tipo { get; init; }
    public decimal? Importe { get; init; }
    public DateTime? Fecha { get; init; }
    public bool Cancelado { get; init; }
    public string? IdConcepto { get; init; }
    public string? Concepto { get; init; }
    public string? Referencia { get; init; }

    public string IdempotencyKey => Folio.ToString();
}

public sealed record CancelledLine
{
    public long? FolioCheque { get; init; }
    public DateTime? Fecha { get; init; }
    public string? Usuario { get; init; }
    public string? IdProducto { get; init; }
    public string? Descripcion { get; init; }
    public decimal? Cantidad { get; init; }
    public decimal? Precio { get; init; }
    public string? Razon { get; init; }
}
