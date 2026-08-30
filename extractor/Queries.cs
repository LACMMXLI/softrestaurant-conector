namespace SoftRestaurant.Extractor;

/// <summary>
/// Consultas SELECT versionadas para la Fase 1 (contrato y extractor de solo lectura).
/// Todas usan rango semiabierto: @Desde incluido, @Hasta excluido — igual que la
/// convención de MAPA_FUNCIONAL_BASE_DATOS_SOFT_RESTAURANT.md §14.
///
/// Filtran por `fecha`/`apertura` (siempre poblada) en vez de `cierre` (puede ser NULL
/// en tickets cancelados o cuentas sin cerrar), porque el objetivo de este extractor es
/// enviar la cabecera completa con su estado — la regla de "venta válida" la aplica el
/// consumidor (API central / reportes), no el extractor.
/// </summary>
internal static class Queries
{
    public const string Cheques = """
        SELECT
            c.WorkspaceId,
            c.folio,
            c.numcheque,
            c.idempresa,
            c.fecha,
            c.cierre,
            c.fechacancelado,
            c.pagado,
            c.cancelado,
            c.facturado,
            c.idturno,
            c.estacion,
            c.idarearestaurant,
            c.mesa,
            c.idmesero,
            c.usuariopago,
            c.foliotempcheques,
            c.subtotal,
            c.subtotalsinimpuestos,
            c.descuento,
            c.descuentoimporte,
            c.totalimpuesto1,
            c.totalimpuestod1,
            c.totalimpuestod2,
            c.totalimpuestod3,
            c.total,
            c.propina,
            c.propinatarjeta,
            c.cargo,
            c.donativo,
            c.cambio,
            c.usuariocancelo,
            c.razoncancelado,
            c.idmotivocancela
        FROM dbo.cheques AS c
        WHERE c.fecha >= @Desde AND c.fecha < @Hasta
        ORDER BY c.folio;
        """;

    public const string CheqDet = """
        SELECT
            d.WorkspaceId,
            d.foliodet,
            d.movimiento,
            d.comanda,
            d.idproducto,
            p.descripcion AS productDescription,
            d.cantidad,
            d.precio,
            d.preciosinimpuestos,
            d.preciocatalogo,
            d.descuento,
            d.hora,
            d.comentario,
            d.idmeseroproducto,
            d.modificador
        FROM dbo.cheqdet AS d
        INNER JOIN dbo.cheques AS c ON c.folio = d.foliodet
        LEFT JOIN dbo.productos AS p ON p.idproducto = d.idproducto
        WHERE c.fecha >= @Desde AND c.fecha < @Hasta
        ORDER BY d.foliodet, d.movimiento;
        """;

    public const string ChequesPagos = """
        SELECT
            cp.WorkspaceId,
            cp.folio,
            cp.idformadepago,
            fp.descripcion AS paymentMethodDescription,
            fp.tipo AS paymentMethodType,
            cp.importe,
            cp.propina,
            cp.tipodecambio,
            cp.referencia,
            cp.cardBrand,
            cp.idturno_cierre
        FROM dbo.chequespagos AS cp
        INNER JOIN dbo.cheques AS c ON c.folio = cp.folio
        LEFT JOIN dbo.formasdepago AS fp ON fp.idformadepago = cp.idformadepago
        WHERE c.fecha >= @Desde AND c.fecha < @Hasta
        ORDER BY cp.folio;
        """;

    public const string Turnos = """
        SELECT
            t.WorkspaceId,
            t.idturnointerno,
            t.idturno,
            t.idestacion,
            t.apertura,
            t.cierre,
            t.cajero,
            t.fondo,
            t.efectivo,
            t.tarjeta,
            t.vales,
            t.credito,
            t.procesado
        FROM dbo.turnos AS t
        WHERE t.apertura >= @Desde AND t.apertura < @Hasta
        ORDER BY t.idturnointerno;
        """;

    public const string DeclaracionCajero = """
        SELECT
            dc.idturnointerno,
            dc.idformadepago,
            dc.tipo,
            dc.importedeclarado,
            dc.tipodecambio
        FROM dbo.declaracioncajero AS dc
        INNER JOIN dbo.turnos AS t ON t.idturnointerno = dc.idturnointerno
        WHERE t.apertura >= @Desde AND t.apertura < @Hasta
        ORDER BY dc.idturnointerno;
        """;

    public const string MovtosCaja = """
        SELECT
            m.folio,
            m.idturno,
            m.tipo,
            m.importe,
            m.fecha,
            m.cancelado,
            m.concepto,
            m.referencia
        FROM dbo.movtoscaja AS m
        WHERE m.fecha >= @Desde AND m.fecha < @Hasta
        ORDER BY m.folio;
        """;

    public const string Cancela = """
        SELECT
            ca.foliocheque,
            ca.fecha,
            ca.usuario,
            ca.clave AS idproducto,
            p.descripcion,
            ca.cantidad,
            ca.precio,
            ca.razon
        FROM dbo.cancela AS ca
        LEFT JOIN dbo.productos AS p ON p.idproducto = ca.clave
        WHERE ca.fecha >= @Desde AND ca.fecha < @Hasta
        ORDER BY ca.fecha;
        """;

    /// <summary>
    /// Totales de control directos desde `cheques`, para el comparador diario
    /// (Fase 1, criterio de salida: "la extracción reproduce venta, tickets, pagos
    /// y cancelados sin duplicar"). Aplica la regla de venta válida del mapa §8.
    /// </summary>
    public const string TotalesControl = """
        SELECT
            COUNT_BIG(*) AS tickets_validos,
            SUM(c.total) AS venta_valida,
            SUM(c.propina) AS propina_valida,
            (SELECT COUNT_BIG(*) FROM dbo.cheques c2
                WHERE c2.fecha >= @Desde AND c2.fecha < @Hasta AND c2.cancelado = 1) AS tickets_cancelados,
            (SELECT COUNT_BIG(*) FROM dbo.cheqdet d
                INNER JOIN dbo.cheques c3 ON c3.folio = d.foliodet
                WHERE c3.fecha >= @Desde AND c3.fecha < @Hasta) AS lineas,
            (SELECT COUNT_BIG(*) FROM dbo.chequespagos cp
                INNER JOIN dbo.cheques c4 ON c4.folio = cp.folio
                WHERE c4.fecha >= @Desde AND c4.fecha < @Hasta) AS filas_pago
        FROM dbo.cheques AS c
        WHERE c.fecha >= @Desde AND c.fecha < @Hasta
          AND c.pagado = 1 AND c.cancelado = 0 AND c.cierre IS NOT NULL;
        """;
}
