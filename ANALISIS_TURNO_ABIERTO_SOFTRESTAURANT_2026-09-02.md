# SoftRestaurant 11: flujo real durante un turno abierto

Fecha de análisis: 2026-09-02. Respaldos comparados:

- `restaurant11.bak` (2026-08-24).
- `02sept2026conturnoabierto/softrestaurant11.bak` (2026-09-02, operación real en curso).

Las copias se restauraron con nombres aislados (`sr11_closed_20260824` y `sr11_open_20260902`) y se dejaron en modo `READ_ONLY`. Todas las consultas de negocio fueron `SELECT`.

## Resultado operativo del respaldo nuevo

El turno abierto está en `turnos`:

| Campo | Valor |
|---|---|
| `idturnointerno` (PK técnica) | `620300` |
| `idturno` (número operativo) | `427` |
| Apertura | `2026-09-01 23:06:15` |
| Cierre | `NULL` |
| Estación | `DESKTOP-L8QJAJ1` |
| Cajero | `JOSEPH` |

Estado capturado:

| Métrica | Resultado |
|---|---:|
| Tickets cobrados, no cancelados | 15 |
| Venta cobrada | $4,545.00 |
| Efectivo | $3,500.00 |
| VISA / tarjeta | $1,045.00 |
| Otras formas | $0.00 |
| Unidades cobradas con precio mayor a cero | 40 |
| Productos distintos cobrados | 28 |
| Cuentas abiertas | 2 |
| Importe pendiente | $775.00 |
| Unidades pendientes con precio mayor a cero | 8 |

La suma de `tempchequespagos.importe` de tickets cobrados es $4,545.00 y reconcilia exactamente con la suma de `tempcheques.total`. No existe todavía ninguna fila de `cheques` con `idturno=427`.

## Tablas y relaciones verificadas

| Tabla | Función durante el turno abierto | Relación comprobada |
|---|---|---|
| `turnos` | Cabecera del turno. `cierre IS NULL` marca el turno abierto. | PK `idturnointerno`; número operativo `idturno`. |
| `tempcheques` | Fuente principal del estado actual. Contiene tickets cobrados y cuentas pendientes. | PK `folio`; `idturno` se informa al pagar, pero permanece `0` en las cuentas aún abiertas observadas. |
| `tempcheqdet` | Productos, cantidades, precios y movimientos de todas las cuentas temporales. | `tempcheqdet.foliodet = tempcheques.folio`; 0 huérfanas. |
| `tempchequespagos` | Desglose de pagos de los tickets ya cobrados. | `tempchequespagos.folio = tempcheques.folio`; 0 huérfanas. |
| `formasdepago` | Catálogo y clasificación del pago. | `tempchequespagos.idformadepago = formasdepago.idformadepago`; tipo 1 efectivo, tipo 2 tarjeta, tipos 3/4 otros. |
| `productos` / `grupos` | Nombre, grupo y clasificación del producto. | `tempcheqdet.idproducto = productos.idproducto`; `productos.idgrupo = grupos.idgrupo`. |
| `cuentas` | Espejo operativo de las cuentas todavía abiertas. En esta captura contiene P03 y JR10. | `cuentas.foliocuenta = tempcheques.folio`; `cuentas.clavemesa = tempcheques.mesa`. |
| `detallescuentas` | Espejo de líneas de las cuentas abiertas para interfaces operativas. | `detallescuentas.clavemesa = cuentas.clavemesa`; coincide con las líneas de folios 12 y 17. |
| `productosenproduccion` | Cola/estado de cocina para productos enviados a monitor. | `folio + movimiento` coincide con el folio temporal y movimiento; solo cubre 50 de 63 líneas, por lo que no es fuente financiera. |
| `cheques`, `cheqdet`, `chequespagos` | Histórico consolidado después del corte. | Cabecera por `folio`; detalle/pagos por ese folio; `cheques.foliotempcheques` conserva el folio temporal de origen. |

`mesas`, `mesasasignadas` y `mapa_mesas` describen catálogo/asignación de mesas, pero no sustituyen a `tempcheques` como autoridad monetaria. Las demás tablas `temp*` estaban vacías en esta captura o no participaron en la conciliación observada.

## Cómo distingue SoftRestaurant los estados

- Ticket cobrado durante turno abierto: `tempcheques.pagado = 1`, `cancelado = 0`, `cierre IS NOT NULL`, `idturno = 427`, con uno o más pagos en `tempchequespagos`.
- Cuenta pendiente: `tempcheques.pagado = 0`, `cancelado = 0`, `cierre IS NULL`, sin pagos. En la captura su `idturno` nativo es `0`.
- Cuenta/ticket cancelado: `cancelado = 1`; nunca se suma a venta ni a pendiente.
- Ticket histórico válido tras consolidación: `cheques.pagado = 1 AND cheques.cancelado = 0 AND cheques.cierre IS NOT NULL`.

Para asociar una cuenta pendiente con `idturno=0`, el conector busca el turno con `cierre IS NULL`, misma estación y `apertura <= tempcheques.fecha`, escogiendo el más reciente. En esta copia solo existe uno y resuelve inequívocamente a `idturno=427`. Esa resolución se aplica también a líneas y pagos, de modo que la clave estable es `427:folio`.

## Evidencia de la transición al corte

El respaldo anterior no está completamente “sin turno abierto”: contiene el turno 411 con `cierre=NULL`, cuatro temporales pagadas por $785 y dos pendientes por $205. El respaldo nuevo permite observar qué ocurrió después:

- Las seis filas temporales antiguas aparecen en `cheques` del turno 411.
- `cheques.foliotempcheques` conserva los folios 1 a 6.
- `WorkspaceId` se conserva exactamente en los seis casos.
- Las cuatro ya pagadas conservaron estado e importe.
- Una pendiente terminó cancelada.
- La otra pendiente pasó de $205 a $245 y terminó pagada.
- Al cerrar el turno 411, `turnos.cierre` quedó en `2026-08-25 02:18:04` y ya no quedaron temporales de ese turno.

Esto demuestra que `tempcheques` es un snapshot mutable y `cheques` es la consolidación histórica. No deben sumarse ambos sin excluir el par `idturno + foliotempcheques`, porque durante una transición podría duplicarse una venta.

## Corrección implementada

- El extractor resuelve el `idturno` operativo de cuentas pendientes mediante `turnos`, estación y fecha, siempre con `SELECT`.
- La API suma como venta del turno abierto únicamente temporales pagadas, no canceladas y con cierre.
- Los pagos temporales alimentan efectivo, tarjeta y otras formas.
- Los productos temporales pagados alimentan top de productos; se excluyen pseudo-líneas de servicio con precio cero.
- Las cuentas abiertas usan exclusivamente `NOT pagado AND NOT cancelado` y su total se reporta por separado.
- Tickets, gráfica horaria y listado reciente incluyen los cobros temporales del turno abierto.
- Se evita duplicar contra `sales` usando `source_shift_id + source_temp_folio`.
- Durante un turno abierto se calcula efectivo esperado, pero no se inventa efectivo declarado ni diferencia de caja antes del corte.

## Validación

- Extracción real de ambos respaldos: conciliada, sin claves duplicadas.
- Consultas del dashboard ejecutadas contra un PostgreSQL 18 aislado con el snapshot real: 15 / $4,545 / $3,500 / $1,045 / 2 / $775.
- `dotnet test`: 90 de 90 pruebas correctas.
- Build API/extractor: correcto.
- Build dashboard React/Vite: correcto.

No se desplegó ni se envió información a producción en este trabajo.
