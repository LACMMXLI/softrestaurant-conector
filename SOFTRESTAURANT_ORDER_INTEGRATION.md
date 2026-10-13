# Integración de pedidos externos con SoftRestaurant

Documento de transferencia para implementar un adaptador de pedidos en un agente local de Windows. Resume exclusivamente la evidencia disponible del laboratorio `CARDONA\\NATIONALSOFT` / `softrestaurant11`; no equivale a una especificación universal de SoftRestaurant.

## Convención de evidencia

- **COMPROBADO**: observado directamente en esta instalación o en una prueba reproducible documentada.
- **INFERIDO**: conclusión apoyada por inspección estática o correlación, sin probar el ciclo completo.
- **PENDIENTE DE VALIDAR**: falta evidencia suficiente. No codificarlo como hecho.

## Instalación y arquitectura observada

- **COMPROBADO** — SQL Server en instancia `CARDONA\\NATIONALSOFT`, base `softrestaurant11`. La versión de la aplicación de escritorio fue Soft Restaurant 11.0.31.
- **COMPROBADO** — Soft Restaurant Móvil 1.5.9 está instalado en `C:\Program Files (x86)\Soft RestaurantMovil 1.4\`.
- **COMPROBADO** — El servicio web IIS `ComanderoMovil` se encontró en `C:\Inetpub\wwwroot\ComanderoMovil`, binding HTTP en puerto 5500 (`Parameters.xml`). El sitio estaba iniciado durante la inspección.
- **COMPROBADO** — Se inspeccionó estáticamente `WebServiceSRCM.dll`, incluida `WebServiceSRCM.Areas.ApiG.Models.DetallesManager`; se observó código relacionado con validación de productos, `foliocuenta`, `procesado`, `enviadosr` y la cola `cuentas_procesar`.
- **PENDIENTE DE VALIDAR** — No se capturó una petición real de la tableta ni se documentó autenticación, rutas, payloads, firma, autorización o respuestas HTTP de `ComanderoMovil`. El puerto y ensamblado son pistas de integración, no una API aprobada.
- **COMPROBADO** — El puente que funcionó fue `cuentas` + `detallescuentas` → señal en `cuentas_procesar` → consumidor SoftRestaurant de escritorio → `tempcheques` + `tempcheqdet` y `productosenproduccion` (KDS).

## Flujo comprobado de inserción

1. **COMPROBADO** — Crear o reutilizar la cabecera auxiliar de `dbo.cuentas` para la mesa/orden. La cuenta de prueba usó mesa `01`, mesero `01` y estación `CARDONA`.
2. **COMPROBADO** — Agregar líneas a `dbo.detallescuentas` usando el procedimiento `dbo.ValidaDetalles`. Este valida el producto y agrega el detalle de buzón. En el laboratorio, `@respuesta = 1` fue aceptación.
3. **COMPROBADO** — Insertar una señal en `dbo.cuentas_procesar` con `idcuenta`, `idcomanda`, cantidad de productos a procesar y `contador=0`. Sin esa señal, la cabecera/detalle auxiliar no se aplicaron a la cuenta operativa.
4. **COMPROBADO** — Confirmar la transacción local. El escritorio consumió la cola asíncronamente; no se ejecutó desde el SQL de inyección una escritura directa a `tempcheques`, `tempcheqdet` ni KDS.
5. **COMPROBADO** — El consumidor vació la fila de cola observada, cambió el detalle origen a `procesado=1` y `enviadosr=1`, materializó cuenta/detalle temporales y creó registros de producción/ruteo.
6. **COMPROBADO** — El resultado fue una cuenta abierta no pagada: `tempcheques.folio=4`, mesa `01`, total/subtotal `175.0000`, `pagado=0`, `cancelado=0`.

La primera línea de prueba fue producto `01001` (`HAMBURGUESASENCILLA`) cantidad 1 a 135.00; generó total 135.00. Luego se agregó `02001` (`COCA 600ML`) cantidad 1 a 40.00; el total terminó en 175.00. La segunda línea se reflejó en alrededor de 1–2 segundos en esa observación; no es un SLA demostrado.

## Tablas y responsabilidades

| Objeto | Papel observado | Claves/campos observados | Estado de conocimiento |
|---|---|---|---|
| `dbo.cuentas` | Cabecera del buzón móvil/auxiliar, previa a la cuenta operativa. | Columnas de cabecera vistas: `clavemesero`, `clavemesa`, `numeropersonas`, `clavearea`, `imprimir`, `estacion`, `prueba`, `tiposervicio`, `total`, `formadepago`, `cantidad`, `cambio`, `idmesa`, `editado`, `enviadosr`, `foliocuenta`, `descuento`, `subtotal`, impuestos, estados de proceso/pago y otros campos. | **COMPROBADO** sobre columnas existentes. Valores completos requeridos, PK y defaults no documentados aquí; revisar el esquema instalado. |
| `dbo.detallescuentas` | Líneas pendientes del buzón. | `clavemesa`, `clave`, `descripcion`, `estacion`, `precio`, `cantidad`, `comentario`, `modificador`, `movimiento`, `tiempo`, `separador`, `hora`, `nivel`, `idproductocompuesto`, `productocompuestoprincipal`, `procesado`, `enviadosr`, `idcomanda`, descuento y más. La prueba mostró `clave`, `movimiento`, `comentario`, `hora`, `procesado`, `enviadosr`. | **COMPROBADO** por firma/SQL del procedimiento y filas antes/después. La semántica de variantes no probadas sigue pendiente. |
| `dbo.cuentas_procesar` | Señal de trabajo para que SoftRestaurant aplique líneas. | Inspección del ensamblado mostró inserción con `id,idcuenta,idcomanda,productos,contador` y valores (`@id`, `@idcuenta`, `@idcomanda`, `@productos`, `0`). La prueba usó `id=1`, cuenta `01`, comanda `0`, productos `1`, contador `0`. | **COMPROBADO** en el flujo demostrado. Semántica de `id`, concurrencia y asignación segura de IDs no probadas. |
| `dbo.tempcheques` | Cuenta operativa temporal que abrió SoftRestaurant. | La prueba leyó `folio`, mesa, total, subtotal, total de alimentos/bebidas, `pagado`, `cancelado`; el folio creado fue 4. | **COMPROBADO** como resultado. No se debe construir su registro manualmente. |
| `dbo.tempcheqdet` | Detalles operativos materializados. | `foliodet=4`, `movimiento` 1/2, `idproducto` 01001/02001, `cantidad=1`, `precio`, `comentario`, `hora`, `folioproduccion`, `comanda_impresa=1`. `folioproduccion` observado 4 y 5. | **COMPROBADO** como resultado del consumidor. Regla de folios de producción no generalizada. |
| `dbo.productosenproduccion` | Filas de ruteo/producción consumidas por KDS. | `folio=4`, movimiento, producto, comentario, `idmonitor`, `estadomonitor`, `horaproduccion`, `enviadomonitor`. 01001 fue a monitor `01`; 02001 a `02`. | **COMPROBADO** para la creación y ruteo inicial. Estado KDS posterior de esta cuenta quedó inicialmente en 0. |
| `dbo.productosmonitores`, `dbo.monitoresproduccion`, `dbo.productos` | Catálogo y asociación producto→monitor. | Monitores `01/COCINA`, `02/BARRA`; inspección registró FK desde producción a productos/monitores. | **COMPROBADO** en estructura/catálogo consultado. La lógica interna de selección completa no está especificada. |

No se constató en los artefactos disponibles una FK necesaria entre `cuentas` y `tempcheques`, ni una regla universal de unicidad de las líneas. No asumas que `clavemesa` sola identifica una orden globalmente: el tipo de cuenta, servicio, comanda y estado pueden influir.

## Procedimiento, cola y consumidor

- **COMPROBADO** — `dbo.ValidaDetalles` recibe `@respuesta int OUTPUT` y parámetros de mesa, producto, descripción, estación, precio, cantidad, comentario, modificador, tiempo, separador, hora, nivel, producto compuesto, comanda y campos asociados.
- **COMPROBADO** — El procedimiento inserta en `detallescuentas` y busca una cabecera pendiente en `cuentas` con `imprimir=0`; valida el producto/bloqueo y calcula el movimiento a partir de detalles existentes para la mesa.
- **COMPROBADO** — El resultado `1` se trató como éxito en los dos inserts de laboratorio. No se documentan aquí todos los códigos de rechazo.
- **COMPROBADO** — SoftRestaurant de escritorio, no el SQL de integración, consumió `cuentas_procesar`. Al insertar cabecera y detalle sin cola, la cuenta temporal vacía apareció pero no se aplicó ninguna línea; al agregar la señal, el detalle llegó a la cuenta y KDS.
- **INFERIDO** — El escritorio mantiene un lector/proceso de cola en ejecución. No se aisló el nombre del ejecutable, hilo o intervalo de sondeo.
- **PENDIENTE DE VALIDAR** — Si se pueden procesar varias cuentas simultáneas, cómo se asigna `id` de cola, semántica del contador, garantías ante caída del consumidor y cómo se reintenta una cola atascada.

La inspección estática de `WebServiceSRCM.dll` corroboró que el servicio móvil también genera señales de cola. Esto no demuestra que el agente nuevo deba emular HTTP directamente ni define el contrato de HTTP.

## SQL documentado de la prueba

El SQL exacto conservado para la segunda línea exitosa fue ejecutado por `sqlcmd` con autenticación integrada contra la instancia/base indicadas. La hora se generó localmente. Valores de producto/comentario anonimizados para la transferencia; identificadores de catálogo son los reales de laboratorio.

```sql
SET NOCOUNT ON;
DECLARE @hora datetime = GETDATE(), @respuesta int;
BEGIN TRY
  BEGIN TRANSACTION;
  EXEC dbo.ValidaDetalles
       @respuesta OUTPUT,
       '01', '02001', 0, 'COCA 600ML', 'CARDONA',
       40.00, 1.0000, 'LAB-LATENCIA-20260924',
       0, '', 0, @hora, 0, '', 0, 0, 0,
       NULL, NULL, NULL;
  IF @respuesta <> 1
    THROW 51003, 'La línea no fue aceptada.', 1;
  INSERT dbo.cuentas_procesar (id, idcuenta, idcomanda, productos, contador)
  VALUES (1, '01', 0, 1, 0);
  COMMIT TRANSACTION;
  SELECT 'EN_COLA' AS result, @hora AS queued_at, @respuesta AS valida_detalles;
END TRY
BEGIN CATCH
  IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
```

**COMPROBADO** — Este fragmento es la segunda línea de un pedido ya abierto. El primer producto también se insertó a través de `dbo.ValidaDetalles` y cola, pero la invocación completa de creación de cabecera no está transcrita de manera verificable en este archivo del proyecto. No copiar el literal `id=1` como generador de ID de cola: se usó en una prueba aislada y la estrategia concurrente no fue validada.

**COMPROBADO** — La cabecera requerida por el procedimiento debía existir con `imprimir=0`. **PENDIENTE DE VALIDAR** — No hay un script completo preservado aquí con todos los valores/columnas insertados en `cuentas`; reproducir la creación exige consultar esquema y Defaults de la instalación destino, o preferir la API móvil documentada.

## Productos, cantidades, precios, impuestos y notas

- **COMPROBADO** — Dos productos de catálogo existentes, cada uno cantidad 1 y precio unitario indicado, dieron total/subtotal 135.00 y 175.00. El total operativo coincidió con la suma de esas líneas.
- **COMPROBADO** — El procedimiento valida producto y calcula el número de movimiento respecto a detalles existentes de mesa; para cuenta de laboratorio creó movimientos 1 y 2.
- **INFERIDO** — SoftRestaurant lleva los importes derivados, folios de producción y monitores al materializar; el agente debe tratar `tempcheques`/KDS como salida de autoridad, no replicar esas reglas.
- **PENDIENTE DE VALIDAR** — Descuentos, impuestos no nulos, IVA incluido, servicio/cargos, productos compuestos, modificadores, medias porciones, notas de cabecera, división de cantidades, fracciones, variantes de precio, redondeo y moneda.
- **PENDIENTE DE VALIDAR** — Un comentario de detalle sí se copió a `tempcheqdet` y `productosenproduccion` en el caso probado. No se demostró visualmente la presentación en todos los KDS ni límites de longitud.
- **COMPROBADO (límite)** — Esta ruta no cobró automáticamente: cuenta resultante `pagado=0`, `cancelado=0`. No se insertó fila de pago en el flujo de buzón demostrado.

## Apertura, procesamiento, pago y cocina

- **COMPROBADO** — El pedido abierto apareció como cuenta temporal en SoftRestaurant con mesa 01 y total 175.00. La cuenta quedó sin pagar.
- **PENDIENTE DE VALIDAR** — La evidencia archivada no deja constancia paso a paso de que se abrió la cuenta desde la UI, se cobró y se imprimió en ese experimento; por tanto, no afirmar que se comprobó el pago de esa cuenta de 175.00.
- **COMPROBADO** — Se generaron los detalles temporales y filas KDS para cocina/barra. En la observación inicial `estadomonitor=0`, `horaproduccion=NULL`, `enviadomonitor=0`: esto prueba creación/ruteo, no despacho, preparación o finalización.
- **INFERIDO** — Los monitores KDS consumen directamente `productosenproduccion` y actualizan su estado; una inspección separada halló estados 2 con hora en otras cuentas finalizadas. No se encontraron triggers/procedimientos SQL que gestionen esas transiciones.
- **PENDIENTE DE VALIDAR** — Si la línea sale a una impresora o KDS depende de configuración local, estatus de producto/monitor, tipo de servicio, estación y flags no explorados.
- **COMPROBADO (contraste)** — Otro documento del proyecto registra una venta manual de servicio rápido que quedó en `tempcheques`, `tempcheqdet` y `tempchequespagos`, pagada/cerrada por 175.00, sin filas de `productosenproduccion`. Por tanto, una cuenta temporal válida y cobrada no implica por sí misma creación de KDS. Esa captura no es evidencia del flujo del buzón móvil.

## Áreas y marca externa `APP`

- **PENDIENTE DE VALIDAR** — No se confirmó un área denominada `APP`, una columna de origen libre y segura ni que el UI/KDS retenga esa etiqueta durante el ciclo.
- **INFERIDO** — `cuentas.clavearea` podría identificar área si existe una fila de catálogo compatible y si el consumidor no la reemplaza. Eso no prueba que agregar un área permita identificar pedidos externos ni que sea inocuo para reportes/impresión.
- **PENDIENTE DE VALIDAR** — Antes de proponer `APP`, inspeccionar el catálogo/relaciones de áreas y crear un caso reversible en laboratorio. Para correlación/idempotencia usar además un identificador externo persistido fuera de tablas nativas si no existe campo dedicado validado.

## Lo que hizo Codex en la prueba exitosa

1. Inspeccionó el servidor/base, el esquema y el procedimiento `dbo.ValidaDetalles`; inspeccionó estáticamente el DLL móvil para identificar la señal `cuentas_procesar`.
2. Creó una cabecera y agregó producto 01001 mediante el procedimiento; comprobó `@respuesta=1`. SoftRestaurant creó `tempcheques.folio=4`, inicialmente vacío.
3. Confirmó que sin señal de cola el detalle no aparecía en la cuenta operativa.
4. Insertó señal de cola en transacción y commit. La cola desapareció al ser consumida, el origen quedó `procesado=1`, `enviadosr=1`, y se materializaron detalle/KDS.
5. Agregó 02001 de la misma forma; verificó el total 175, los dos detalles y monitores 01 y 02.

No se escribió directamente en `tempcheques`, `tempcheqdet` ni `productosenproduccion` para crear el pedido móvil exitoso. La prueba estaba autorizada como laboratorio; no autoriza escrituras equivalentes en producción.

## Idempotencia, validaciones y riesgos

- **COMPROBADO** — La cola es consumida y eliminada; después de un timeout puede ser imposible saber sólo por su ausencia si el consumidor aceptó el pedido. Se debe reconciliar con origen y cuenta operativa antes de reintentar.
- **INFERIDO / recomendación** — Mantener un registro durable propio con `branchId`, `externalOrderId`, hash de payload, estado, fecha y folio SoftRestaurant resultante. Aplicar unicidad sobre `(sucursal, externalOrderId)` en el almacén del agente/servicio; nunca inventar unicidad en tablas históricas de SoftRestaurant.
- **PENDIENTE DE VALIDAR** — No se comprobó una columna nativa que conserve el ID externo a través de toda la cuenta. Comentarios/área no son sustitutos seguros sin validar su longitud y persistencia.
- **COMPROBADO (riesgo)** — Inserción de cabecera/detalles sin cola deja un pedido auxiliar incompleto. Insertar operación temporal directamente evita la transformación observada y omite cálculo/ruteo nativo.
- **INFERIDO (riesgo)** — Repetir `ValidaDetalles` después de timeout puede duplicar línea y total si la primera solicitud sí fue aceptada. El movimiento no constituye clave idempotente global.

Antes de escribir, validar instalación/versiones y conexión local; estado de la sucursal y turno; mesa/área/mesero/servicio activos; catálogo de productos; disponibilidad/bloqueo; precio vigente y precisión; cantidad positiva y límites; notas; tipos de descuento/impuesto soportados; que el pedido externo no exista ya; que la cabecera destino no esté cerrada o pagada; y que la ruta configurada (buzón móvil/API) corresponda a esta versión.

## Qué no debe hacer el agente nuevo

- No escribir directamente `tempcheques`, `tempcheqdet`, pagos, KDS ni tablas históricas para simular la ruta probada.
- No saltarse la señal `cuentas_procesar` tras escribir buzón; tampoco afirmar aceptación porque `cuentas`/`detallescuentas` existan.
- No usar `id=1`, `folio=4`, movimientos 1/2 o folios 4/5 como valores constantes ni asumir que son próximos consecutivos en otra sucursal/turno.
- No inferir columnas/valores requeridos por la instalación desde esta lista parcial; revisar `sys.columns`, defaults, constraints, índices y procedimiento en destino.
- No confiar en una API HTTP no capturada, aceptar SQL arbitrario desde servicio central, ni exponer SQL Server a Internet.
- No cobrar, cancelar, imprimir o cerrar el pedido automáticamente; esos pasos no pertenecen al flujo probado.
- No reintentar ciegamente una operación con resultado ambiguo; consultar reconciliación primero.

## Verificación posterior a escritura

Usar consultas de sólo lectura parametrizadas, con un identificador local durable y marca/ventana de tiempo para acotar filas:

1. Leer `cuentas`/`detallescuentas`: cabecera pendiente esperada, número de líneas y `imprimir=0` durante el procesamiento; luego revisar flags `procesado` y `enviadosr`.
2. Leer `cuentas_procesar`: la fila encolada debe dejar de estar pendiente tras procesamiento. Su ausencia sola no acredita éxito.
3. Encontrar un `tempcheques` nuevo por relación/filtros comprobados para esta instalación; verificar `pagado=0`, `cancelado=0`, mesa, total y estado abierto.
4. Verificar `tempcheqdet`: conjunto de productos, cantidad, precio, comentarios y suma que cuadre con la cuenta según reglas tributarias conocidas.
5. Verificar `productosenproduccion` cuando aplique: una fila por detalle esperado y monitor correcto. Reportar `estadomonitor=0` como creado/enrutado, no como enviado/preparado.
6. Confirmar en UI que la cuenta puede abrirse y dejar que el cajero procese/cobre según flujo del negocio. El laboratorio no documenta una consulta universal de vínculo de la cola al folio; no correlacionar sólo por folio temporal reciclable.

## Evidencias disponibles en este proyecto

- [SOFT_RESTAURANT_LAB_FINDINGS.md](./SOFT_RESTAURANT_LAB_FINDINGS.md) — correlaciones KDS, estados y límites.
- [DOCUMENTACION_TECNICA_HALLAZGOS_SOFTRESTAURANT_2026-09-23.md](./DOCUMENTACION_TECNICA_HALLAZGOS_SOFTRESTAURANT_2026-09-23.md) — hallazgos del visor y estructura operacional.
- [Captura 1 - Venta Servicio Rapido.md](./Captura%201%20-%20Venta%20Servicio%20Rapido.md) — contraste de venta manual pagada, no evidencia de inserción por buzón.
- [LINEA_BASE_SOFTRESTAURANT_SERVICIO_RAPIDO_2026-09-22.md](./LINEA_BASE_SOFTRESTAURANT_SERVICIO_RAPIDO_2026-09-22.md) — línea base relacionada.
- Registro del experimento de buzón: hilo de laboratorio `01a0d6ee-d50b-77f3-8f04-afa0912f6324` (24–26 sep 2026), instalación/base antes indicada. Este registro contiene SQL de prueba y salida de `sqlcmd`; no hay un script independiente con todos los inserts de cabecera.

## CONTRATO PARA EL NUEVO MÓDULO DEL SINCRONIZADOR

### Entrada

El módulo debe aceptar un objeto de pedido normalizado con:

- `externalOrderId` estable y único por canal/sucursal.
- `branchId`/identidad de instalación local; no aceptar un servidor/base libre aportado por el canal.
- `createdAt`, `requestedAt` opcional, tipo de servicio y destino (mesa sólo cuando aplique).
- `waiterId` o usuario configurado localmente cuando el flujo lo requiera.
- `items[]`: `productId` de SoftRestaurant, cantidad decimal positiva, opciones/modificadores/notas sólo si el perfil local los permite.
- Precio esperado y moneda como datos de contraste, no autorización para sobrescribir el catálogo sin política explícita.
- Datos de cliente estrictamente necesarios, si se habilita ese escenario.

### Validaciones obligatorias

1. Instalación compatible y conexión al SQL Server local disponible.
2. `externalOrderId` no procesado antes; comparar también hash del contenido ante repetición.
3. Catálogo, producto habilitado, precio y cantidad válidos; rechazar opciones/modificadores no mapeados.
4. Mesa/área/mesero/servicio válidos para el estado actual.
5. Restricciones de esquema del buzón y semántica de `cuentas_procesar` comprobadas para la versión instalada.
6. El pedido resultante debe quedar abierto/no pagado. El importe calculado por SoftRestaurant debe conciliarse con la política de precios/impuestos aceptada antes de reportar éxito.

### Operaciones locales

1. Crear registro durable de idempotencia `RECEIVED` antes de tocar SoftRestaurant.
2. En transacción local, crear/obtener cabecera auxiliar en estado pendiente; llamar `dbo.ValidaDetalles` por cada línea en una transacción compatible; si cualquiera rechaza, revertir toda la unidad de inserción cuando sea seguro.
3. Insertar señal en `cuentas_procesar` una vez para la cuenta/lote correcto; no hardcodear su ID y no encolar parcialmente sin definir recuperación.
4. Confirmar commit y pasar a `QUEUED`.
5. Sondear con timeout acotado y backoff configurable las tablas de origen/operativas; no mantener una transacción abierta durante la espera.
6. Conciliar cabecera/detalles/total/flags/KDS y guardar el folio/relaciones observadas. La acción debe ser reentrante: ante estado ambiguo, reconciliar, nunca duplicar a ciegas.

**Nota de compatibilidad:** la transacción conjunta que incluya el procedimiento y cola se usó con éxito para una línea adicional. No se ha demostrado atomicidad completa de creación de cabecera + varias líneas + múltiples señales ni manejo concurrente. El módulo debe validar esta estrategia en laboratorio por versión antes de producción.

### Estados de salida

- `RECEIVED` — solicitud durable, aún no escrita.
- `VALIDATION_FAILED` — datos no compatibles; incluye errores por campo.
- `WRITING` — transacción de buzón iniciada.
- `QUEUED` — escritura confirmada y señal insertada.
- `ACCEPTED_OPEN` — SoftRestaurant materializó cuenta abierta; devolver `folio` observado, total, líneas y estado de conciliación.
- `REJECTED_BY_SOFTRESTAURANT` — procedimiento rechazó producto/detalle o no se materializó antes del límite comprobado.
- `RECONCILIATION_REQUIRED` — timeout o estado contradictorio; no reintentar automáticamente.
- `DUPLICATE` — externalOrderId ya existe y payload equivalente; regresar referencia original.
- `CONFLICT` — externalOrderId existente con payload diferente.

### Errores que debe modelar

- `INSTALLATION_UNSUPPORTED`, `DB_UNAVAILABLE`, `SCHEMA_MISMATCH`, `SHIFT_OR_TABLE_INVALID`.
- `PRODUCT_UNKNOWN`, `PRODUCT_BLOCKED`, `PRICE_MISMATCH`, `QUANTITY_INVALID`, `OPTION_UNSUPPORTED`.
- `DUPLICATE_ORDER`, `IDEMPOTENCY_CONFLICT`, `VALIDA_DETALLES_REJECTED`.
- `QUEUE_INSERT_FAILED`, `DB_TRANSACTION_FAILED`, `SOFTRESTAURANT_TIMEOUT`, `ORDER_NOT_MATERIALIZED`, `TOTAL_MISMATCH`, `KDS_ROUTE_MISSING`.
- `AMBIGUOUS_COMMIT` — se perdió la respuesta después del commit; reconciliar antes de permitir cualquier reintento.

El módulo no debe devolver `ACCEPTED_OPEN` por el mero éxito del commit local. Ese estado requiere evidencia de materialización y conciliación con las tablas operativas.

## PREGUNTAS ABIERTAS

- ¿Cuál es el contrato HTTP real de `ComanderoMovil` (rutas, autenticación, payload, errores y garantías de reintento)?
- ¿Cuál es el conjunto completo de campos/defaults/constraints requerido para crear la cabecera `cuentas` en otra instalación/version?
- ¿Cómo se asigna de forma segura y concurrente `cuentas_procesar.id`? ¿Cuál es la semántica de `productos` y `contador` cuando hay varios productos/comandas?
- ¿Cómo correlaciona internamente el consumidor la cabecera auxiliar con `tempcheques`, y cómo distinguir duplicado frente a timeout tras commit?
- ¿Qué genera `cuentas` y qué consecutivos/folios usa en cuentas de mesa, mostrador, domicilio y varias sucursales/turnos?
- ¿Puede usarse un área `APP` sin efectos secundarios y queda persistida/visible durante UI, reportes, impresión y cierre?
- ¿Cómo se representan y calculan impuestos, descuentos, modificadores, compuestos, fracciones, cargo de servicio y redondeos?
- ¿La cuenta de folio 4 del buzón llegó a abrirse manualmente y se cobró desde UI? ¿Cuál es el ciclo de impresión/cierre y destino consolidado `cheques`?
- ¿Qué condiciones determinan creación de KDS frente a impresión, y cuáles son las transiciones exactas de monitores para pedidos nuevos?
- ¿Qué hace el sistema si falla a mitad de lote, si el escritorio está cerrado o si ya existe una cuenta abierta en la mesa?

