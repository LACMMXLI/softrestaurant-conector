# Diagnóstico forense: productos cancelados de SoftRestaurant

Fecha de análisis: 2026-09-05.  Alcance: consultas `SELECT` sobre los respaldos aislados en solo lectura `sr11_closed_20260824` y `sr11_open_20260902`. No se modificó código, esquema ni datos.

## Resultado

`dbo.cancela` es la fuente primaria y persistente de cada línea cancelada. No se debe excluir ningún registro con `foliocheque = 0`.

Sin embargo, en esta instalación el valor `0` no representa un cheque histórico que pueda recuperarse después: las 88 filas con ese valor corresponden a 30 acciones de cancelación seguidas por **Eliminación de cuenta**. La cuenta fue retirada antes de convertirse en un cheque histórico, por lo que no existe un `dbo.cheques.folio` correcto que asignarles. El reporte debe conservarlas como cancelaciones válidas con cuenta identificada por auditoría, pero con cheque histórico `NULL` y estado `ACCOUNT_DELETED_BEFORE_CHECK`.

## Tablas que participan realmente

| Tabla | Papel comprobado | Enlace fiable |
|---|---|---|
| `dbo.cancela` | Bitácora histórica de líneas canceladas; 515 filas en el respaldo abierto. | Fuente del evento: fecha, usuario, producto (`clave`), cantidad, precio, comanda, razón y, si existe, `foliocheque`. No tiene PK ni FK. |
| `dbo.cheques` | Cabecera histórica de una cuenta que sí se consolidó. | `cheques.folio = cancela.foliocheque` cuando este último es distinto de cero: 427/427 filas no-cero encontraron cabecera. `numcheque` es otro identificador, no sustituye al folio interno. |
| `dbo.cheqdet` | Detalle histórico de un cheque consolidado. | `cheqdet.foliodet = cheques.folio`; sirve para contexto de producto/comanda, pero una línea eliminada puede no permanecer en el detalle. |
| `dbo.tempcheques` / `dbo.tempcheqdet` | Cuenta y detalle vivos durante un turno abierto. | `tempcheques.folio = tempcheqdet.foliodet`. El folio es temporal; tras consolidar se conserva en `cheques.foliotempcheques`. |
| `dbo.tempcancela` | Cancelación de una cuenta aún transitoria. | `tempcancela.foliocheque = tempcheques.folio`; estaba vacía en este respaldo, por lo que la transición de una cancelación temporal no se pudo observar aquí. |
| `dbo.cuentas` / `dbo.detallescuentas` | Espejo operativo de cuentas abiertas. | `cuentas.foliocuenta = tempcheques.folio`; `detallescuentas.clavemesa = cuentas.clavemesa`. Son estado mutable, no histórico. |
| `dbo.bitacorasistema` | Prueba de auditoría de la acción y de la cuenta nominal. | Evento `Comedor - Cancelación de productos`, fecha/hora, `valores` (`Cuenta: ... , Mesero: ...`), estación, usuario solicitante y `numcheque` si existe. |
| `dbo.turnos` | Contexto operativo del turno. | `cheques.idturno`, y para transitorios el `idturno` propio o el turno abierto de misma estación/fecha. |

## Qué ocurre por estado de la cuenta

### Cuenta abierta

La cuenta vive en `tempcheques` y sus líneas en `tempcheqdet`; `cuentas` y `detallescuentas` son espejos de la operación. Una cancelación activa se puede obtener de `tempcancela` por su folio temporal. El extractor debe enviarla como observación transitoria/snapshot mientras exista, nunca como venta histórica.

### Cuenta pagada o cerrada

Al consolidar, la cabecera aparece en `cheques`, los detalles en `cheqdet` y el vínculo de transición queda en `cheques.foliotempcheques`. Las cancelaciones que ya tienen `cancela.foliocheque > 0` se relacionan de forma determinista por la clave interna `cheques.folio`; no se debe usar `bitacorasistema.numcheque` como sustituto porque puede ser cero aun cuando el folio interno sí existe.

### Cuenta eliminada antes de convertirse en cheque

SoftRestaurant conserva el evento de productos en `cancela`, pero puede eliminar la cuenta en vez de consolidarla. En ese caso la bitácora documenta la misma cuenta y la eliminación posterior; no queda cabecera en `cheques`, ni cheque temporal vigente. `foliocheque = 0` permanece como cero porque no hay un folio histórico que actualizar.

## Evidencia real

### Cobro/consolidación: el evento de bitácora puede tener número cero, pero `cancela` sí tiene folio interno

El 2026-05-28 a las 21:00:24 se canceló el producto `18015`, cantidad 1, $100, razón `Mesa: N11`.

| Origen | Valores |
|---|---|
| `cancela` | `foliocheque=13824`, usuario `JOSEPH`. |
| `cheques` por `folio=13824` | `numcheque=13634`, mesa `N11`, turno `240`, folio temporal `54`, abierta 20:09:24 y cerrada 21:02:33. |
| `bitacorasistema` al mismo segundo | `Comedor - Cancelación de productos`, `Cuenta: N11, Mesero: 03`, pero `numcheque=0`. |

Esto prueba que `bitacorasistema.numcheque=0` no invalida la cancelación ni anula el enlace principal ya disponible en `cancela.foliocheque`.

### Cuenta eliminada: no existe cheque recuperable

El 2026-05-28 a las 21:08:05 se insertaron dos líneas en `cancela` para `Mesa: CARLOS`: productos `01050` ($0) y `23155` ($100), ambos con `foliocheque=0`. La bitácora registra un segundo después `Comedor - Cancelación de productos | Cuenta: CARLOS, Mesero: 05 | numcheque=0`; a las 21:10:06 registra `Eliminación de cuenta | Cuenta: CARLOS`.

No hay `cheques` cuyo intervalo de vida contenga esa acción para la mesa CARLOS, ni cabecera que pueda enlazarse. Asignar los cheques anteriores de la misma mesa habría sido falso: la mesa/nombre se reutiliza.

### Cobertura y permanencia del cero

- `cancela`: 515 filas; 427 con folio distinto de cero y cabecera histórica coincidente; 88 con `foliocheque=0`.
- Las 88 filas cero forman 30 acciones distintas por `fecha + mesa extraída de razon`.
- Las 30/30 acciones correlacionan con `bitacorasistema` por cuenta y diferencia de 0 a 2 segundos.
- Las 30/30 tienen evento posterior `Eliminación de cuenta` de la misma cuenta dentro de diez minutos; ninguna termina en `Cancelación de cuenta` en esa ventana.
- Los dos respaldos prueban persistencia: el respaldo cerrado tiene 88 ceros y el abierto tiene los mismos 88; cero filas previas al 2026-08-25 desaparecieron o se actualizaron a un folio distinto de cero.

## Investigación de objetos SQL

No se encontró procedimiento, función, trigger o vista con DML explícito (`INSERT`, `UPDATE` o `DELETE`) sobre `dbo.cancela`:

- No hay triggers sobre `cancela`, `tempcancela`, `tempcheques`, `cheques`, `cheqdet` o sus detalles temporales.
- No existen dependencias SQL declaradas hacia `cancela` ni FK entrantes/salientes.
- Cinco vistas mencionan el texto `cancela`, pero son vistas de reportes/operación y no escriben: `vwrepproductosvendidoscheques`, `vwrepproductosvendidostempcheques`, `vwrepventascheques`, `vwrepventastempcheques` y `vwwmponermesas`.

La inserción/eliminación observada proviene, por tanto, del binario de SoftRestaurant o de SQL dinámico externo; los metadatos del respaldo no permiten atribuirla a un objeto SQL almacenado.

## Estrategia segura para el Agente Restaurante (propuesta, no implementada)

1. Mantener `cancela` como fuente obligatoria e incluir todas sus filas, incluso `foliocheque=0`.
2. Resolver primero `cancela.foliocheque > 0` exclusivamente por `cheques.folio`; publicar además `numcheque`, `mesa`, turno y `foliotempcheques` como contexto, sin sustituir la llave interna.
3. Para `foliocheque=0`, correlacionar el grupo de líneas de la misma acción usando `fecha` exacta, cuenta extraída de `razon`, usuario, producto, cantidad, precio y comanda; buscar el evento de bitácora `Comedor - Cancelación de productos` de la misma cuenta dentro de una tolerancia máxima de dos segundos. Guardar la cuenta/mesa, mesero, estación y usuario solicitante como evidencia.
4. Antes de proponer un cheque, exigir una cabecera única cuya vida contenga el instante de la acción y que concuerde simultáneamente en cuenta/mesa, estación, mesero y —si existe— comanda/producto/cantidad/precio. Si falta cualquiera de esos criterios o hay más de una candidata, dejar `source_check_folio=NULL`; nunca elegir por nombre de mesa solamente.
5. Si la bitácora posterior registra `Eliminación de cuenta` (caso demostrado), clasificar `ACCOUNT_DELETED_BEFORE_CHECK`; no intentar recuperar ni fabricar un cheque. Sigue siendo una cancelación válida y visible en el reporte.
6. Durante un turno abierto extraer `tempcancela` como snapshot junto con `tempcheques` y `tempcheqdet`, ligado por folio temporal. Cuando aparezca posteriormente la versión histórica, deduplicar por una huella de evento (origen, instante, usuario, producto, cantidad, precio, comanda y razón), conservando la observación histórica como canónica.
7. Presentar tres estados de cobertura: `LINKED_TO_CHECK`, `LINKED_TO_ACTIVE_TEMP_ACCOUNT` y `ACCOUNT_DELETED_BEFORE_CHECK`/`UNRESOLVED`. El último no es cero ni se excluye de los totales de productos cancelados; solo carece legítimamente de cheque.

## Límite verificado

La propuesta permite que ninguna cancelación válida se pierda. No puede ni debe afirmar que toda cancelación corresponde a un cheque: las 88 filas analizadas prueban lo contrario. Para validar el caso futuro de `tempcancela` durante una cancelación en cuenta aún abierta se necesita una nueva captura de solo lectura tomada antes de eliminar o cerrar dicha cuenta.
