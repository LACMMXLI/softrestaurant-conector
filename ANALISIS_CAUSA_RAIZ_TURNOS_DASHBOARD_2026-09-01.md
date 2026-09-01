# Análisis de causa raíz: turnos sin ventas ni productos en el dashboard

Fecha de validación: 2026-09-01 (America/Tijuana)

## Resultado ejecutivo

El agente, la ingestión y PostgreSQL sí estaban funcionando. La causa raíz estaba en el API del dashboard: el selector enviaba el identificador interno de la fila de turno de SoftRestaurant (`idturnointerno`, por ejemplo `610300`), pero las ventas están relacionadas por el número operativo/comercial del turno (`idturno`, por ejemplo `425`).

`DashboardReportService.GetMetaAsync` localizaba correctamente la fila mediante `source_shift_id = 610300`, pero después reutilizaba `610300` como filtro de ventas, líneas, pagos y movimientos. Esas tablas contienen `425`, por lo que las consultas devolvían cero aunque los datos existían.

La corrección conserva ambos conceptos:

- El identificador interno se usa únicamente para seleccionar una fila de `shifts`.
- El `idTurno` del payload se usa para relacionar y filtrar ventas, productos, pagos, cuentas transitorias y movimientos.
- Para registros históricos anteriores que no tengan `payload.idTurno`, se conserva `source_shift_id` como respaldo compatible.

## Flujo comprobado de extremo a extremo

1. SoftRestaurant
   - Los tickets cerrados salen de `cheques`, con sus detalles y pagos.
   - Las cuentas abiertas salen de `tempcheques`, `tempcheqdet` y `tempchequespagos` como una fotografía transitoria completa.
   - Los turnos salen de `turnos` y contienen dos identificadores distintos: `idturnointerno` e `idturno`.

2. Agente/conector
   - `ExtractionJob` incorpora ventas, líneas, pagos, turnos y estado transitorio al `SyncBatch`.
   - `SyncCoordinator` y el outbox conservan y envían el lote a `/api/ingestion/batches`.

3. API e ingestión
   - `BatchIngestor` persiste el lote de forma transaccional.
   - Los logs de producción confirmaron `POST /api/ingestion/batches` con HTTP 200 después del despliegue.
   - Los heartbeats siguieron respondiendo HTTP 200.

4. PostgreSQL
   - La fila del turno cerrado tenía `source_shift_id = 610300` y `payload.idTurno = 425`.
   - Las ventas de ese turno estaban almacenadas con turno `425`, no `610300`.
   - La fila del turno abierto tenía `source_shift_id = 610301` y `payload.idTurno = 426`.

5. Dashboard
   - El frontend conserva el identificador interno como valor del selector, para seleccionar una fila sin ambigüedad.
   - El API ahora devuelve y usa el número comercial correcto para las consultas y para la presentación.

## Evidencia real de producción

| Comprobación | Resultado |
|---|---:|
| Turno interno seleccionado | 610300 |
| Número comercial resuelto | 425 |
| Tickets almacenados | 30 |
| Tickets válidos mostrados | 28 |
| Venta válida | $7,975.00 |
| Líneas de producto | 106 |
| Unidades | 110 |
| Efectivo mostrado | $4,645.00 |
| Tarjeta mostrada | $3,330.00 |

En la interfaz de producción se abrió el ticket real `26116` y se comprobaron sus productos, entre ellos `021 AHH...PLEBEES BURGER`, `DRAGON ROLL`, `024 HOT CHEETOS CHEESE BURGER` y `COCA COLA 600ML`, además de su pago en efectivo por $1,055.00.

El turno abierto actual `426` se muestra correctamente como `Turno 426`. En el lote y PostgreSQL observados tenía cero tickets, cero ventas y cero cuentas transitorias. Por tanto, el cero visible para ese turno es fiel a SoftRestaurant; no se generaron ni alteraron datos para simular actividad.

## Corrección aplicada

- `central-api/DashboardReportService.cs`
  - Prioriza `payload.idTurno` como número comercial visible.
  - Separa el identificador interno seleccionado del número comercial usado por las consultas.
  - Añade una resolución compatible con registros históricos sin `idTurno`.
- `auth-tests/DashboardSecurityTests.cs`
  - Cubre la relación moderna `610301 -> 426`.
  - Cubre el respaldo histórico cuando sólo existe un identificador.

Commit desplegado: `bbbaddab05471665667248b76b6b7ca25726f33b` (`fix: map dashboard shifts to business shift numbers`).

## Validaciones finales

- Pruebas .NET: 80/80 aprobadas.
- Build del dashboard: aprobado.
- Coolify: despliegue manual del recurso independiente `restaurant-agent-api` finalizado con estado `Success`.
- Health check público: HTTP 200 con `{"status":"ok"}`.
- Ingestión posterior al despliegue: HTTP 200.
- Heartbeat posterior al despliegue: HTTP 200.
- Prueba visual autenticada:
  - Selector e encabezado muestran `Turno 425`/`Turno 426`, no `610300`/`610301`.
  - El turno 425 muestra ventas, tickets, pagos, movimientos y productos reales.
  - El turno 426 permanece en cero porque el origen realmente no reportó actividad en ese momento.

No se modificó PostgreSQL, no se tocaron datos de negocio y no se desplegaron el agente, el dashboard ni el administrador.
