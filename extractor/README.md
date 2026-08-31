# SoftRestaurant Sync Agent

Extractor y emisor de **solo lectura** contra `softrestaurant11`. Conserva el modo
local validado de la Fase 1 y añade el piloto de sincronización hacia la API central.
[PLAN_DESARROLLO_SISTEMA_REPORTES_SOFT_RESTAURANT.md](../PLAN_DESARROLLO_SISTEMA_REPORTES_SOFT_RESTAURANT.md):
consultas versionadas, contrato JSON normalizado, prueba de llaves idempotentes y
comparador diario contra los totales de Soft Restaurant.

No modifica datos ni objetos. Todas las consultas son `SELECT`.

## Uso

```bash
dotnet run -c Release -- --desde 2026-08-01 --hasta 2026-08-27 --out ./out
```

Extraer, conciliar y enviar una vez:

```powershell
$env:SRX_API_URL='https://reportes-api.example.com'
$env:SRX_ACTIVATION_KEY='clave-de-un-solo-uso'
$env:SRX_MACHINE_NAME=$env:COMPUTERNAME
dotnet run -c Release -- --desde 2026-08-27 --hasta 2026-08-27 --send
```

Ejecutar de forma periódica (consola o Servicio de Windows):

```powershell
dotnet run -c Release -- --watch
```

`--watch` relee una ventana móvil de tres días cada 60 segundos. Cada lote se guarda
primero en SQLite (`data/sync-queue.db`) y sólo se elimina después de que la API lo
confirma. Si Internet falla, el siguiente ciclo reintenta con espera incremental.

Sin argumentos, extrae por defecto el rango **[ayer, hoy]**.

### Parámetros

| Parámetro | Descripción | Default |
|---|---|---|
| `--desde` / `--hasta` | Rango semiabierto sobre `fecha`/`apertura` (hasta se interpreta inclusive y se convierte internamente a exclusivo) | ayer → hoy |
| `--server` | Instancia SQL Server | `CARDONA\SQLEXPRESS` |
| `--database` | Base de datos | `softrestaurant11` |
| `--user` / `--password` | Credenciales SQL (si se omiten, usa Windows Auth) | ninguno |
| `--trusted` | Fuerza Windows Auth aunque haya `--user` | — |
| `--out` | Carpeta de salida | `./out` |
| `--send` | Encola y envía el lote después de conciliar | desactivado |
| `--watch` | Modo periódico compatible con Servicio de Windows | desactivado |
| `--api-url` / `SRX_API_URL` | URL HTTPS de la API central | — |
| `--machine-name` / `SRX_MACHINE_NAME` | Identificador auditable del equipo | nombre de Windows |
| `--installation-id` / `SRX_INSTALLATION_ID` | Identidad de dispositivo asignada al vincular | — |
| `--device-token` / `SRX_DEVICE_TOKEN` | Token permanente cifrado del dispositivo | — |
| `--branch` / `SRX_BRANCH_CODE` | Código recibido al vincular el equipo | — (vacío hasta vincular) |
| `--queue` / `SRX_QUEUE_PATH` | Archivo SQLite de cola | `./data/sync-queue.db` |
| `--interval` | Segundos entre ciclos (mínimo 15) | `60` |
| `--rolling-days` | Días recientes que se releen (1–30) | `3` |

También se pueden fijar por variables de entorno `SRX_SQL_SERVER`, `SRX_SQL_DATABASE`,
`SRX_SQL_USER`, `SRX_SQL_PASSWORD`, o editando `appsettings.json`.

**Ya no hay activación por clave.** El instalador solo cifra con DPAPI la conexión SQL y la URL
de la API — sin ninguna credencial de dispositivo. Un equipo recién instalado arranca en
estado "no vinculado" (`ExtractorConfig.Linked = false`): `SyncWorker`/`HeartbeatWorker` esperan
sin intentar enviar nada hasta que la GUI (`extractor-ui`) complete el flujo de vinculación
(login con la cuenta del SaaS → elegir sucursal → "Vincular este equipo"), que entrega la
credencial al servicio vía `POST http://127.0.0.1:<puerto>/link` en `AgentControlServer`. A
partir de ahí el servicio persiste `SRX_INSTALLATION_ID`, `SRX_BRANCH_CODE`, `SRX_BUSINESS_ID` y
`SRX_DEVICE_TOKEN` en el archivo DPAPI y los usa en cada llamada (`X-Connector-Id` +
`Authorization: Bearer`) — no existe ningún modo legacy de token compartido.

Para importar manualmente una credencial de dispositivo (soporte/recuperación) sin pasar por la
GUI, ejecutando como la cuenta del servicio:

```powershell
Stop-Service SoftRestaurantSyncAgent
& 'C:\Program Files\Fatboy\SoftRestaurant Sync Agent\SoftRestaurant.Extractor.exe' `
  --import-connector-credential .\credencial.json
Start-Service SoftRestaurantSyncAgent
```

El JSON debe contener `installationId`, `branchCode`, `businessId` (opcional) y `deviceToken`
tal como los devuelve `POST /api/web/branches/{branchCode}/link-device` (o `.../replace-device`,
o el equivalente de soporte `POST /api/admin/branches/{branchCode}/replace-device`). Tras
protegerlos con DPAPI, el agente elimina automáticamente ese archivo.

**Producción actual:** el instalador permite usar el mismo login SQL configurado en
SoftRestaurant. El agente conserva una lista fija de consultas `SELECT` y no ejecuta
`INSERT`/`UPDATE`/`DELETE`/`EXECUTE`/`ALTER` contra la base origen. Usar una credencial con
permisos más amplios eleva el impacto de una filtración, por lo que el equipo debe mantenerse
restringido a administradores del negocio.

## Qué extrae

| Archivo | Tabla origen | Filas (respaldo completo) |
|---|---|---:|
| `ventas.json` | `cheques` | 24,961 |
| `lineas.json` | `cheqdet` | 88,287 |
| `pagos.json` | `chequespagos` | 24,787 |
| `turnos.json` | `turnos` | 368 |
| `declaraciones_cajero.json` | `declaracioncajero` | 845 |
| `movimientos_caja.json` | `movtoscaja` | 3,632 |
| `cancelaciones.json` | `cancela` (cruda, sin PK — ver nota abajo) | 498 |
| `reconciliacion.json` | comparador de totales | — |

Cada entidad conserva sus campos fuente (`source_*` implícitos vía los nombres
originales) para auditoría, y expone una `idempotencyKey` calculada según la sección 8
del plan (`WorkspaceId` cuando existe; si no, la llave compuesta documentada).

`cancela` no tiene PK y puede contener filas idénticas legítimas (sección 6 del plan):
esta salida es una extracción cruda del rango, no una llave idempotente por fila. El
agente real debe enviarla como snapshot agregado y reemplazar la partición de fechas.

## Verificación incluida

Cada corrida:

1. Cuenta duplicados en cada `idempotencyKey` (debe ser 0).
2. Recalcula tickets válidos, venta, propina, cancelados, líneas y filas de pago
   directamente en SQL (`Queries.TotalesControl`) y los compara contra lo extraído.
   Sale con código de salida `1` si algo difiere.

Verificado contra el respaldo restaurado (05-feb a 24-ago-2026): 24,442 tickets válidos,
$6,956,703.50, 519 cancelados — coincide exactamente con
[MAPA_FUNCIONAL_BASE_DATOS_SOFT_RESTAURANT.md](../MAPA_FUNCIONAL_BASE_DATOS_SOFT_RESTAURANT.md).
Dos corridas consecutivas producen salida idéntica byte a byte (reenviar es seguro).

## Límites actuales del piloto

- Extracción de catálogo (`productos`, `productosdetalle`, `grupos`) — no incluida aquí,
  el plan la agrupa en `/ingestion/catalog` por separado.
- El servicio, la cola y el instalador gráfico ya funcionan. El instalador autodetecta
  `restaurant.ini`, solicita las credenciales SQL de SoftRestaurant y guarda la configuración
  cifrada con DPAPI para la máquina local. El registro del servicio conserva únicamente la
  ruta del archivo protegido.
- El backend incluye ingesta, estado de sincronización y resumen del día. Usuarios,
  roles y dashboard visual quedan fuera de este piloto.
