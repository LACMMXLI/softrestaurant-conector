using Microsoft.Data.SqlClient;
using RestaurantAgent.Sync.Contracts;

namespace RestaurantAgent.Extractor;

internal sealed class Extractor(string connectionString, DateTime desde, DateTime hasta)
{
    public async Task<List<ProductCatalogItem>> ExtractProductsAsync(CancellationToken ct)
    {
        var result = new List<ProductCatalogItem>();
        await ForEachRowAsync(Queries.Products, r => result.Add(new ProductCatalogItem
        {
            IdProducto = r.GetStringOrNull("idproducto")!,
            Descripcion = r.GetStringOrNull("descripcion"),
            IdGrupo = r.GetStringOrNull("idgrupo"),
            Grupo = r.GetStringOrNull("groupDescription"),
            Clasificacion = r.GetInt32OrNull("clasificacion"),
            Activo = r.GetBool("activo")
        }), ct);
        return result;
    }

    public async Task<List<SaleHeader>> ExtractSalesAsync(CancellationToken ct)
    {
        var result = new List<SaleHeader>();
        await ForEachRowAsync(Queries.Cheques, r => result.Add(new SaleHeader
        {
            WorkspaceId = r.GetStringOrNull("WorkspaceId"),
            Folio = r.GetInt64("folio"),
            NumCheque = r.GetStringOrNull("numcheque"),
            IdEmpresa = r.GetStringOrNull("idempresa"),
            Fecha = r.GetDateTimeOrNull("fecha"),
            Cierre = r.GetDateTimeOrNull("cierre"),
            FechaCancelado = r.GetDateTimeOrNull("fechacancelado"),
            Pagado = r.GetBool("pagado"),
            Cancelado = r.GetBool("cancelado"),
            Facturado = r.GetBool("facturado"),
            IdTurno = r.GetInt32OrNull("idturno"),
            Estacion = r.GetStringOrNull("estacion"),
            IdAreaRestaurant = r.GetStringOrNull("idarearestaurant"),
            Mesa = r.GetStringOrNull("mesa"),
            IdMesero = r.GetStringOrNull("idmesero"),
            UsuarioPago = r.GetStringOrNull("usuariopago"),
            FoliotTempCheques = r.GetStringOrNull("foliotempcheques"),
            Subtotal = r.GetDecimalOrNull("subtotal"),
            SubtotalSinImpuestos = r.GetDecimalOrNull("subtotalsinimpuestos"),
            Descuento = r.GetDecimalOrNull("descuento"),
            DescuentoImporte = r.GetDecimalOrNull("descuentoimporte"),
            TotalImpuesto1 = r.GetDecimalOrNull("totalimpuesto1"),
            TotalImpuestoD1 = r.GetDecimalOrNull("totalimpuestod1"),
            TotalImpuestoD2 = r.GetDecimalOrNull("totalimpuestod2"),
            TotalImpuestoD3 = r.GetDecimalOrNull("totalimpuestod3"),
            Total = r.GetDecimalOrNull("total"),
            Propina = r.GetDecimalOrNull("propina"),
            PropinaTarjeta = r.GetDecimalOrNull("propinatarjeta"),
            Cargo = r.GetDecimalOrNull("cargo"),
            Donativo = r.GetDecimalOrNull("donativo"),
            Cambio = r.GetDecimalOrNull("cambio"),
            UsuarioCancelo = r.GetStringOrNull("usuariocancelo"),
            RazonCancelado = r.GetStringOrNull("razoncancelado"),
            IdMotivoCancela = r.GetStringOrNull("idmotivocancela"),
        }), ct);
        return result;
    }

    public async Task<List<SaleLine>> ExtractSaleLinesAsync(CancellationToken ct)
    {
        var result = new List<SaleLine>();
        await ForEachRowAsync(Queries.CheqDet, r => result.Add(new SaleLine
        {
            WorkspaceId = r.GetStringOrNull("WorkspaceId"),
            FolioDet = r.GetInt64("foliodet"),
            Movimiento = r.GetInt32("movimiento"),
            Comanda = r.GetInt32OrNull("comanda"),
            IdProducto = r.GetStringOrNull("idproducto"),
            DescripcionProducto = r.GetStringOrNull("productDescription"),
            Cantidad = r.GetDecimalOrNull("cantidad"),
            Precio = r.GetDecimalOrNull("precio"),
            PrecioSinImpuestos = r.GetDecimalOrNull("preciosinimpuestos"),
            PrecioCatalogo = r.GetDecimalOrNull("preciocatalogo"),
            Descuento = r.GetDecimalOrNull("descuento"),
            Hora = r.GetDateTimeOrNull("hora"),
            Comentario = r.GetStringOrNull("comentario"),
            IdMeseroProducto = r.GetStringOrNull("idmeseroproducto"),
            Modificador = r.GetStringOrNull("modificador"),
        }), ct);
        return result;
    }

    public async Task<List<SalePayment>> ExtractSalePaymentsAsync(CancellationToken ct)
    {
        var result = new List<SalePayment>();
        await ForEachRowAsync(Queries.ChequesPagos, r => result.Add(new SalePayment
        {
            WorkspaceId = r.GetStringOrNull("WorkspaceId"),
            Folio = r.GetInt64("folio"),
            IdFormaDePago = r.GetStringOrNull("idformadepago"),
            DescripcionFormaDePago = r.GetStringOrNull("paymentMethodDescription"),
            TipoFormaDePago = r.GetInt32OrNull("paymentMethodType"),
            Importe = r.GetDecimalOrNull("importe"),
            Propina = r.GetDecimalOrNull("propina"),
            TipoDeCambio = r.GetDecimalOrNull("tipodecambio"),
            Referencia = r.GetStringOrNull("referencia"),
            CardBrand = r.GetStringOrNull("cardBrand"),
            IdTurnoCierre = r.GetInt32OrNull("idturno_cierre"),
        }), ct);
        return result;
    }

    public async Task<List<TransientSaleHeader>> ExtractTransientSalesAsync(CancellationToken ct)
    {
        var result = new List<TransientSaleHeader>();
        await ForEachRowAsync(Queries.TempCheques, r => result.Add(new TransientSaleHeader
        {
            WorkspaceId = r.GetStringOrNull("WorkspaceId"),
            TempFolio = r.GetInt64("folio"),
            NumCheque = r.GetStringOrNull("numcheque"),
            Fecha = r.GetDateTimeOrNull("fecha"),
            Cierre = r.GetDateTimeOrNull("cierre"),
            Pagado = r.GetBool("pagado"),
            Cancelado = r.GetBool("cancelado"),
            IdTurno = r.GetInt32OrNull("idturno"),
            CuentaEnUso = r.GetBool("cuentaenuso"),
            CuentaPagadaProcesada = r.GetBoolOrNull("cuentapagadaprocesada"),
            Estacion = r.GetStringOrNull("estacion"),
            Mesa = r.GetStringOrNull("mesa"),
            IdMesero = r.GetStringOrNull("idmesero"),
            UsuarioPago = r.GetStringOrNull("usuariopago"),
            Subtotal = r.GetDecimalOrNull("subtotal"),
            Total = r.GetDecimalOrNull("total"),
            Propina = r.GetDecimalOrNull("propina")
        }), ct);
        return result;
    }

    public async Task<List<TransientSaleLine>> ExtractTransientSaleLinesAsync(CancellationToken ct)
    {
        var result = new List<TransientSaleLine>();
        await ForEachRowAsync(Queries.TempCheqDet, r => result.Add(new TransientSaleLine
        {
            WorkspaceId = r.GetStringOrNull("WorkspaceId"),
            HeaderWorkspaceId = r.GetStringOrNull("headerWorkspaceId"),
            TempFolio = r.GetInt64("foliodet"),
            IdTurno = r.GetInt32OrNull("idturno"),
            Movimiento = r.GetInt32("movimiento"),
            Comanda = r.GetInt32OrNull("comanda"),
            IdProducto = r.GetStringOrNull("idproducto"),
            DescripcionProducto = r.GetStringOrNull("productDescription"),
            Cantidad = r.GetDecimalOrNull("cantidad"),
            Precio = r.GetDecimalOrNull("precio"),
            Descuento = r.GetDecimalOrNull("descuento"),
            Hora = r.GetDateTimeOrNull("hora"),
            Comentario = r.GetStringOrNull("comentario")
        }), ct);
        return result;
    }

    public async Task<List<TransientSalePayment>> ExtractTransientSalePaymentsAsync(CancellationToken ct)
    {
        var result = new List<TransientSalePayment>();
        await ForEachRowAsync(Queries.TempChequesPagos, r => result.Add(new TransientSalePayment
        {
            WorkspaceId = r.GetStringOrNull("WorkspaceId"),
            HeaderWorkspaceId = r.GetStringOrNull("headerWorkspaceId"),
            TempFolio = r.GetInt64("folio"),
            IdTurno = r.GetInt32OrNull("idturno"),
            IdFormaDePago = r.GetStringOrNull("idformadepago"),
            DescripcionFormaDePago = r.GetStringOrNull("paymentMethodDescription"),
            TipoFormaDePago = r.GetInt32OrNull("paymentMethodType"),
            Importe = r.GetDecimalOrNull("importe"),
            Propina = r.GetDecimalOrNull("propina"),
            TipoDeCambio = r.GetDecimalOrNull("tipodecambio"),
            Referencia = r.GetStringOrNull("referencia"),
            CardBrand = r.GetStringOrNull("cardBrand")
        }), ct);
        return result;
    }

    public async Task<List<Shift>> ExtractShiftsAsync(CancellationToken ct)
    {
        var result = new List<Shift>();
        await ForEachRowAsync(Queries.Turnos, r => result.Add(new Shift
        {
            WorkspaceId = r.GetStringOrNull("WorkspaceId"),
            IdTurnoInterno = r.GetInt32("idturnointerno"),
            IdTurno = r.GetInt32("idturno"),
            IdEstacion = r.GetStringOrNull("idestacion"),
            Apertura = r.GetDateTimeOrNull("apertura"),
            Cierre = r.GetDateTimeOrNull("cierre"),
            Cajero = r.GetStringOrNull("cajero"),
            Fondo = r.GetDecimalOrNull("fondo"),
            Efectivo = r.GetDecimalOrNull("efectivo"),
            Tarjeta = r.GetDecimalOrNull("tarjeta"),
            Vales = r.GetDecimalOrNull("vales"),
            Credito = r.GetDecimalOrNull("credito"),
            Procesado = r.GetBoolOrNull("procesado"),
        }), ct);
        return result;
    }

    public async Task<List<CashierDeclaration>> ExtractCashierDeclarationsAsync(CancellationToken ct)
    {
        var result = new List<CashierDeclaration>();
        await ForEachRowAsync(Queries.DeclaracionCajero, r => result.Add(new CashierDeclaration
        {
            IdTurnoInterno = r.GetInt32("idturnointerno"),
            IdFormaDePago = r.GetStringOrNull("idformadepago"),
            Tipo = r.GetInt32OrNull("tipo"),
            ImporteDeclarado = r.GetDecimalOrNull("importedeclarado"),
            TipoDeCambio = r.GetDecimalOrNull("tipodecambio"),
        }), ct);
        return result;
    }

    public async Task<List<CashMovement>> ExtractCashMovementsAsync(CancellationToken ct)
    {
        var result = new List<CashMovement>();
        await ForEachRowAsync(Queries.MovtosCaja, r => result.Add(new CashMovement
        {
            Folio = r.GetInt64("folio"),
            IdTurno = r.GetInt32OrNull("idturno"),
            Tipo = r.GetInt32("tipo"),
            Importe = r.GetDecimalOrNull("importe"),
            Fecha = r.GetDateTimeOrNull("fecha"),
            Cancelado = r.GetBool("cancelado"),
            IdConcepto = null, // movtoscaja no tiene idconcepto; `concepto` ya es el texto libre
            Concepto = r.GetStringOrNull("concepto"),
            Referencia = r.GetStringOrNull("referencia"),
        }), ct);
        return result;
    }

    public async Task<List<CancelledLine>> ExtractCancellationsAsync(CancellationToken ct)
    {
        var result = new List<CancelledLine>();
        await ForEachRowAsync(Queries.Cancela, r => result.Add(ReadCancellation(r)), ct);
        await ForEachRowAsync(Queries.TempCancela, r => result.Add(ReadCancellation(r)), ct);
        return result;
    }

    private static CancelledLine ReadCancellation(RowReader r) => new()
        {
            SourceKind = r.GetStringOrNull("sourceKind")!,
            FolioCheque = r.GetInt64OrNull("foliocheque"),
            FolioTemporal = r.GetInt64OrNull("folioTemporal"),
            SaleDetailId = r.GetStringOrNull("saledetailid"),
            Comanda = r.GetStringOrNull("comanda"),
            Fecha = r.GetDateTimeOrNull("fecha"),
            Usuario = r.GetStringOrNull("usuario"),
            IdProducto = r.GetStringOrNull("idproducto"),
            Descripcion = r.GetStringOrNull("descripcion"),
            Cantidad = r.GetDecimalOrNull("cantidad"),
            Precio = r.GetDecimalOrNull("precio"),
            Razon = r.GetStringOrNull("razon"),
            IdMotivoCancela = r.GetStringOrNull("idmotivocancela"),
            MotivoDescripcion = r.GetStringOrNull("motivoDescripcion"),
            IdTurno = r.GetInt32OrNull("idturno"),
            IdAreaRestaurant = r.GetStringOrNull("idarearestaurant"),
            AreaDescripcion = r.GetStringOrNull("areaDescripcion"),
            IdEmpresa = r.GetStringOrNull("idempresa"), EmpresaNombre = r.GetStringOrNull("empresaNombre"),
            CuentaAbiertaEn = r.GetDateTimeOrNull("cuentaAbiertaEn"), CuentaCerradaEn = r.GetDateTimeOrNull("cuentaCerradaEn"),
            CuentaPagada = r.GetBoolOrNull("cuentaPagada"), CuentaCancelada = r.GetBoolOrNull("cuentaCancelada"),
            TotalFinalCuenta = r.GetDecimalOrNull("totalFinalCuenta"),
            CuentaReferencia = r.GetStringOrNull("cuentaReferencia"),
            EstadoCorrelacion = r.GetStringOrNull("estadoCorrelacion") ?? "UNRESOLVED_HISTORICAL",
            EventoCorrelacionEn = r.GetDateTimeOrNull("eventoCorrelacionEn"),
        };

    public async Task<ControlTotals> ExtractControlTotalsAsync(CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(Queries.TotalesControl, conn);
        AddRangeParams(cmd);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var r = new RowReader(reader);
            return new ControlTotals(
                TicketsValidos: r.GetInt64OrNull("tickets_validos") ?? 0,
                VentaValida: r.GetDecimalOrNull("venta_valida") ?? 0m,
                PropinaValida: r.GetDecimalOrNull("propina_valida") ?? 0m,
                TicketsCancelados: r.GetInt64OrNull("tickets_cancelados") ?? 0,
                Lineas: r.GetInt64OrNull("lineas") ?? 0,
                FilasPago: r.GetInt64OrNull("filas_pago") ?? 0);
        }
        return new ControlTotals(0, 0m, 0m, 0, 0, 0);
    }

    private async Task ForEachRowAsync(string sql, Action<RowReader> map, CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
        AddRangeParams(cmd);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var row = new RowReader(reader);
        while (await reader.ReadAsync(ct))
        {
            map(row);
        }
    }

    private void AddRangeParams(SqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("@Desde", desde);
        cmd.Parameters.AddWithValue("@Hasta", hasta);
    }
}

internal readonly record struct ControlTotals(
    long TicketsValidos,
    decimal VentaValida,
    decimal PropinaValida,
    long TicketsCancelados,
    long Lineas,
    long FilasPago);

/// <summary>Envoltorio con accesores tolerantes a NULL y a columnas ausentes por versión de esquema.</summary>
internal readonly struct RowReader(SqlDataReader reader)
{
    private int? IndexOf(string name)
    {
        for (int i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), name, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return null;
    }

    private bool IsNull(string name, out int idx)
    {
        var i = IndexOf(name);
        idx = i ?? -1;
        return i is null || reader.IsDBNull(i.Value);
    }

    // Varias columnas "numéricas" de Soft Restaurant son en realidad char/varchar de ancho
    // fijo (p.ej. `cheqdet.comanda`) y contienen espacios en blanco en vez de NULL cuando no
    // aplican. Estos accesores tratan cadenas vacías/blancas como ausencia de valor.
    private object? RawOrNull(string name)
    {
        if (IsNull(name, out var i)) return null;
        var value = reader.GetValue(i);
        if (value is string s)
        {
            var trimmed = s.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
        return value;
    }

    public string? GetStringOrNull(string name) => RawOrNull(name)?.ToString();
    public long GetInt64(string name) => GetInt64OrNull(name) ?? 0;
    public long? GetInt64OrNull(string name) => RawOrNull(name) is { } v ? Convert.ToInt64(v) : null;
    public int GetInt32(string name) => GetInt32OrNull(name) ?? 0;
    public int? GetInt32OrNull(string name) => RawOrNull(name) is { } v ? Convert.ToInt32(v) : null;
    public decimal? GetDecimalOrNull(string name) => RawOrNull(name) is { } v ? Convert.ToDecimal(v) : null;
    public DateTime? GetDateTimeOrNull(string name) => RawOrNull(name) is { } v ? Convert.ToDateTime(v) : null;
    public bool GetBool(string name) => GetBoolOrNull(name) ?? false;
    public bool? GetBoolOrNull(string name) => RawOrNull(name) is { } v ? Convert.ToBoolean(v) : null;
}
