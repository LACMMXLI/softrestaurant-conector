namespace RestaurantAgent.Extractor;

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
    public const string Products = """
        SELECT
            p.idproducto,
            p.descripcion,
            p.idgrupo,
            g.descripcion AS groupDescription,
            g.clasificacion,
            CAST(1 AS bit) AS activo
        FROM dbo.productos AS p
        LEFT JOIN dbo.grupos AS g ON g.idgrupo = p.idgrupo
        ORDER BY p.idproducto;
        """;

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

    // tempcheques es una tabla de estado actual, no histórica. Se extrae completa en cada
    // ciclo para que el servidor pueda retirar cuentas que cambiaron, se cancelaron o ya
    // fueron trasladadas a cheques.
    public const string TempCheques = """
        SELECT
            t.WorkspaceId,
            t.folio,
            t.numcheque,
            t.fecha,
            t.cierre,
            t.pagado,
            t.cancelado,
            COALESCE(NULLIF(t.idturno, 0), open_shift.idturno) AS idturno,
            t.cuentaenuso,
            t.cuentapagadaprocesada,
            t.estacion,
            t.mesa,
            t.idmesero,
            t.usuariopago,
            t.subtotal,
            t.total,
            t.propina
        FROM dbo.tempcheques AS t
        OUTER APPLY (
            SELECT TOP (1) s.idturno
            FROM dbo.turnos AS s
            WHERE s.cierre IS NULL
              AND s.apertura <= COALESCE(t.fecha, GETDATE())
              AND s.idestacion = t.estacion
            ORDER BY s.apertura DESC, s.idturnointerno DESC
        ) AS open_shift
        ORDER BY t.folio;
        """;

    public const string TempCheqDet = """
        SELECT
            d.WorkspaceId,
            t.WorkspaceId AS headerWorkspaceId,
            d.foliodet,
            COALESCE(NULLIF(t.idturno, 0), open_shift.idturno) AS idturno,
            d.movimiento,
            d.comanda,
            d.idproducto,
            p.descripcion AS productDescription,
            d.cantidad,
            d.precio,
            d.descuento,
            d.hora,
            d.comentario
        FROM dbo.tempcheqdet AS d
        INNER JOIN dbo.tempcheques AS t ON t.folio = d.foliodet
        OUTER APPLY (
            SELECT TOP (1) s.idturno
            FROM dbo.turnos AS s
            WHERE s.cierre IS NULL
              AND s.apertura <= COALESCE(t.fecha, GETDATE())
              AND s.idestacion = t.estacion
            ORDER BY s.apertura DESC, s.idturnointerno DESC
        ) AS open_shift
        LEFT JOIN dbo.productos AS p ON p.idproducto = d.idproducto
        ORDER BY d.foliodet, d.movimiento;
        """;

    public const string TempChequesPagos = """
        SELECT
            tp.WorkspaceId,
            t.WorkspaceId AS headerWorkspaceId,
            tp.folio,
            COALESCE(NULLIF(t.idturno, 0), open_shift.idturno) AS idturno,
            tp.idformadepago,
            fp.descripcion AS paymentMethodDescription,
            fp.tipo AS paymentMethodType,
            tp.importe,
            tp.propina,
            tp.tipodecambio,
            tp.referencia,
            tp.cardBrand
        FROM dbo.tempchequespagos AS tp
        INNER JOIN dbo.tempcheques AS t ON t.folio = tp.folio
        OUTER APPLY (
            SELECT TOP (1) s.idturno
            FROM dbo.turnos AS s
            WHERE s.cierre IS NULL
              AND s.apertura <= COALESCE(t.fecha, GETDATE())
              AND s.idestacion = t.estacion
            ORDER BY s.apertura DESC, s.idturnointerno DESC
        ) AS open_shift
        LEFT JOIN dbo.formasdepago AS fp ON fp.idformadepago = tp.idformadepago
        ORDER BY tp.folio, tp.idformadepago;
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
            'HISTORICAL' AS sourceKind,
            ca.foliocheque,
            c.foliotempcheques AS folioTemporal,
            CAST(NULL AS varchar(250)) AS saledetailid,
            ca.comanda,
            ca.fecha,
            ca.usuario,
            ca.clave AS idproducto,
            p.descripcion,
            ca.cantidad,
            ca.precio,
            ca.razon,
            CONVERT(varchar(max), ca.idmotivocancela) AS idmotivocancela,
            mc.descripcion AS motivoDescripcion,
            c.idturno,
            c.idarearestaurant,
            ar.descripcion AS areaDescripcion,
            c.idempresa,
            e.nombre AS empresaNombre,
            c.fecha AS cuentaAbiertaEn,
            c.cierre AS cuentaCerradaEn,
            c.pagado AS cuentaPagada,
            c.cancelado AS cuentaCancelada,
            c.total AS totalFinalCuenta
        FROM dbo.cancela AS ca
        LEFT JOIN dbo.cheques AS c ON c.folio = ca.foliocheque
        LEFT JOIN dbo.productos AS p ON p.idproducto = ca.clave
        LEFT JOIN dbo.motivoscancelacion AS mc ON mc.idmotivocancela = CONVERT(varchar(5), ca.idmotivocancela)
        LEFT JOIN dbo.areasrestaurant AS ar ON ar.idarearestaurant = c.idarearestaurant
        LEFT JOIN dbo.empresas AS e ON e.idempresa = c.idempresa
        WHERE ca.fecha >= @Desde AND ca.fecha < @Hasta
        ORDER BY ca.fecha;
        """;

    // tempcancela es estado operativo: no se filtra por fecha porque una cancelación de una
    // cuenta aún abierta debe permanecer visible aunque el turno continúe otro día. El agente
    // manda este conjunto como snapshot completo y la API retira solo las observaciones
    // transitorias que ya no existan.
    public const string TempCancela = """
        SELECT
            'TRANSIENT' AS sourceKind,
            CAST(NULL AS bigint) AS foliocheque,
            t.folio AS folioTemporal,
            ca.saledetailid,
            ca.comanda,
            ca.fecha,
            ca.usuario,
            ca.clave AS idproducto,
            p.descripcion,
            ca.cantidad,
            ca.precio,
            ca.razon,
            CONVERT(varchar(max), ca.idmotivocancela) AS idmotivocancela,
            mc.descripcion AS motivoDescripcion,
            COALESCE(NULLIF(t.idturno, 0), open_shift.idturno) AS idturno,
            t.idarearestaurant,
            ar.descripcion AS areaDescripcion,
            t.idempresa,
            e.nombre AS empresaNombre,
            t.fecha AS cuentaAbiertaEn,
            t.cierre AS cuentaCerradaEn,
            t.pagado AS cuentaPagada,
            t.cancelado AS cuentaCancelada,
            t.total AS totalFinalCuenta
        FROM dbo.tempcancela AS ca
        LEFT JOIN dbo.tempcheques AS t ON t.folio = ca.foliocheque
        OUTER APPLY (SELECT TOP (1) s.idturno FROM dbo.turnos s WHERE s.cierre IS NULL
            AND s.apertura <= COALESCE(t.fecha, GETDATE()) AND s.idestacion = t.estacion
            ORDER BY s.apertura DESC, s.idturnointerno DESC) AS open_shift
        LEFT JOIN dbo.productos AS p ON p.idproducto = ca.clave
        LEFT JOIN dbo.motivoscancelacion AS mc ON mc.idmotivocancela = CONVERT(varchar(5), ca.idmotivocancela)
        LEFT JOIN dbo.areasrestaurant AS ar ON ar.idarearestaurant = t.idarearestaurant
        LEFT JOIN dbo.empresas AS e ON e.idempresa = t.idempresa
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
