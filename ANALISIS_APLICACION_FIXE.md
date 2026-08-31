# Análisis técnico de la aplicación FIXE para RestaurantAgent

## 1. Objetivo del análisis

Este documento registra los hallazgos obtenidos al analizar la aplicación localizada en:

```text
C:\Extraido
```

El propósito es comprender cómo resuelve la extracción y sincronización de información desde RestaurantAgent, comparar su enfoque con el sistema que se está desarrollando en este proyecto y determinar qué ideas pueden reutilizarse sin copiar sus debilidades de seguridad, confiabilidad y calidad de datos.

El análisis fue exclusivamente estático:

- No se instaló ni ejecutó el MSI.
- No se ejecutaron `FlowFormsApp.exe`, `FService.exe` ni `wv.exe`.
- No se hicieron solicitudes a los servidores de FIXE.
- No se modificó la base de datos de RestaurantAgent.
- No se encontraron instrucciones SQL de escritura en los ensamblados analizados.

## 2. Conclusión ejecutiva

La aplicación es un conector de extracción y sincronización para reportes. Instala un servicio de Windows que consulta periódicamente una API central, recibe una lista de conjuntos de datos solicitados, ejecuta consultas `SELECT` contra la base local de RestaurantAgent y envía los resultados por HTTPS en paquetes.

La solución confirma que la arquitectura general que se está desarrollando es viable:

1. Un agente local instalado en Windows.
2. Acceso de solo lectura a RestaurantAgent.
3. Comunicación saliente con una API central.
4. Identificación de cada sucursal mediante un token.
5. Envío por lotes de ventas, pagos, catálogos, inventarios y datos operativos.

No obstante, el producto analizado presenta riesgos que no deben replicarse:

- Credenciales y token almacenados en texto plano.
- Uso de la cuenta administrativa `sa` con una contraseña predeterminada.
- Ejecuciones del temporizador que pueden superponerse.
- Excepciones ignoradas y errores silenciosos.
- Ausencia de cola local, reintentos e idempotencia demostrable.
- Conversión indiscriminada de datos a texto.
- Consultas amplias mediante `SELECT *`.
- Errores de cableado entre solicitudes, repositorios y endpoints.
- Manejo incorrecto y fijo de la zona horaria.
- Ejecutables principales e instalador sin firma digital.

La recomendación es usar esta aplicación como referencia de comportamiento e instalación, pero mantener como base técnica el extractor tipado, conciliado e idempotente que ya existe en este proyecto.

## 3. Inventario de la aplicación

### 3.1 Archivos principales

| Archivo | Función inferida y verificada | Firma |
|---|---|---|
| `Instalador.msi` | Instala archivos, ejecuta prerequisitos, registra el servicio y abre el configurador | Sin firma |
| `FlowFormsApp.exe` | Interfaz WinForms/WebView2 para activar y configurar la sucursal | Sin firma |
| `FService.exe` | Servicio Windows que extrae y sincroniza datos | Sin firma |
| `wv.exe` | Instalador de Microsoft Edge WebView2 | Firma válida de Microsoft |
| `C:\Fixe\Config\Config.xml` | Configuración de sucursal, SQL Server, token y tamaños de paquete | No aplica |

### 3.2 Metadatos del instalador

| Propiedad | Valor |
|---|---|
| Producto MSI | `Monitor` |
| Fabricante | `FIXE Soluciones` |
| Versión del producto | `1.4.0` |
| Framework requerido | .NET Framework 4.6.2 |
| Instalación principal | `C:\Resources` y `C:\Fixe` |
| Servicio | `FService` |
| Inicio del servicio | Automático |
| Cuenta de servicio | `LocalService` |
| Descripción | Servicio de sincronización con la API |

### 3.3 Huellas SHA-256

Estas huellas permiten identificar exactamente los archivos analizados:

| Archivo | SHA-256 |
|---|---|
| `Instalador.msi` | `9D15D860264034934A90EC105A6B5B8D6D550A8ACD986331FCDDCD5140610555` |
| `FlowFormsApp.exe` | `3DAE6412BBB4ECE4404592DF9A6815DC38C4E8ADD7D9AA58A2D31D4F970A6909` |
| `FService.exe` | `51E56FEE848A89783ACED608A130DA5CFF524E330A1D3E2170771A4CB337D1B7` |
| `wv.exe` | `389601CBD7E9256CE22348E3CEB2C33E39DDC7A8C75DB897D269DC23B17AD11D` |

## 4. Arquitectura encontrada

```text
Instalador MSI 1.4.0
    |
    +-- Instala WebView2 y dependencias .NET
    +-- Instala C:\Resources\FlowFormsApp.exe
    +-- Instala C:\Resources\FService.exe
    +-- Instala C:\Fixe\Config\Config.xml
    +-- Registra e inicia el servicio FService
    +-- Abre FlowFormsApp.exe
             |
             +-- Abre portal de activación mediante WebView2
             +-- Obtiene token y sucursal desde la URL final
             +-- Detecta la instalación local de RestaurantAgent
             +-- Lee DataSource y Catalog desde restaurant.ini
             +-- Actualiza C:\Fixe\Config\Config.xml
                      |
                      v
              Servicio FService
                      |
                      +-- Se ejecuta cada 5 minutos
                      +-- Consulta sync_sucursales en la nube
                      +-- Recibe fechas y banderas de extracción
                      +-- Ejecuta SELECT contra RestaurantAgent
                      +-- Divide resultados en paquetes
                      +-- Envía cada paquete a la API central
```

## 5. Configuración inicial y detección de RestaurantAgent

`FlowFormsApp.exe` abre la siguiente página dentro de WebView2:

```text
https://licenciapps.com/FIXELandingApp/home/index2
```

Cuando la navegación llega a una URL que contiene la palabra `done`, la aplicación toma los parámetros:

```text
token
sucursalid
```

Después busca las siguientes rutas, en este orden de preferencia:

```text
C:\nationalsoft\Softrestaurant11.0\restaurant.ini
C:\nationalsoft\Softrestaurant10.0\restaurant.ini
C:\nationalsoft\Softrestaurant9.5.0Pro\restaurant.ini
```

Del archivo `restaurant.ini` lee:

- `Catalog`: nombre de la base de datos.
- `DataSource`: servidor o instancia SQL Server.

También intenta leer el puerto desde el Registro de Windows:

```text
HKLM\SOFTWARE\Microsoft\Microsoft SQL Server\NATIONALSOFT\MSSQLServer\SuperSocketNetLib\Tcp
```

Sin embargo, el repositorio SQL construye la conexión usando solamente servidor, base, usuario y contraseña. El puerto guardado en el XML no se utiliza explícitamente en la cadena de conexión.

## 6. Servicio de sincronización

### 6.1 Funcionamiento

El servicio registrado se llama `FService`, se ejecuta como `LocalService` y se configura para inicio automático.

Su temporizador tiene un intervalo fijo de:

```text
300000 ms = 5 minutos
```

En cada ciclo intenta ejecutar `MigrateData()`.

### 6.2 Consulta de instrucciones

El servicio realiza un `POST` a:

```text
https://fixe.pro/backend/api/conector/sync_sucursales
```

El cuerpo incluye:

```json
{
  "token": "[token de la instalación]",
  "sucursales_id": "[id de sucursal]"
}
```

La respuesta esperada contiene:

- `dates.from`: inicio del periodo en Unix timestamp.
- `dates.to`: final del periodo en Unix timestamp.
- `solicitudes`: banderas que indican qué entidades deben extraerse.

### 6.3 Envío por lotes

El tamaño predeterminado es de 250 elementos por paquete. Cada paquete incorpora:

```json
{
  "token": "[token]",
  "sucursales_id": "[sucursal]",
  "nombre_de_entidad": []
}
```

El servicio envía los paquetes secuencialmente, pero no comprueba de manera confiable que el servidor los haya persistido.

## 7. Cobertura funcional

### 7.1 Ventas y operación

| Solicitud | Tabla o consulta local | Endpoint |
|---|---|---|
| `cheques` | `cheques` | `set_cheques` |
| `cheques_temp` | `tempcheques` | `set_chequestmp` |
| `cheques_detalles` | `cheqdet` | `set_chequesdet` |
| `cheques_detalles_temp` | `tempcheqdet` | `set_chequesDetTmp` |
| `cancela` | `cancela` | `set_cancela` |
| `cancelatmp` | `tempcancela` | `set_cancelatmp` |
| `turnos` | `turnos` | `set_turnos` |
| `horariosturnos` | `horariosturnos` | `set_horariosturnos` |
| `meseros` | `meseros` | `set_meseros` |
| `bitacorasistena` | `bitacorasistema` | `set_bitacorasistema` |

Los cheques incluyen datos como:

- Folio y número de cheque.
- Fecha y cierre.
- Mesa y número de personas.
- Mesero, turno y estación.
- Estado pagado, cancelado e impreso.
- Subtotal, total, descuentos, cortesías y propinas.
- Totales por efectivo, tarjeta, vales y otros medios.
- Usuarios de apertura, pago, descuento y cancelación.
- Pagos relacionados desde `chequespagos`.

### 7.2 Catálogos

| Solicitud | Tabla local | Endpoint |
|---|---|---|
| `productos` | `productos` | `set_productos` |
| `productosdetalle` | `productos` | `set_productosdetalle` |
| `grupos` | `grupos` | `set_grupos` |
| `areas` | `areas` | `set_areas` |
| `areas_restaurant` | `areasrestaurant` | `set_areas_restaurant` |
| `formas_pago` | `formasdepago` | `set_formasdepago` |
| `tipodescuento` | `tipodescuento` | `set_tipodescuento` |

### 7.3 Inventarios y almacenes

| Solicitud | Tabla o consulta local | Endpoint |
|---|---|---|
| `almacenes` | `almacen` | `set_almacenes` |
| `insumos` | `insumos` | `set_insumos` |
| `insumosdetalle` | `insumosdetalle` | `set_insumosdetalle` |
| `insumospresentaciones` | `insumospresentaciones` | `set_insumospresentaciones` |
| `insumopresentaciondetalle` | `insumospresentacionesdetalle` | `set_insumoPresentacionesDetalle` |
| `stockinsumos` | `stockinsumos` | `set_stockinsumos` |
| `costos` | `costos` | `set_costos` |
| `movsinv` | `movsinv` | `set_movsinv` |
| `movtosalmacen` | `movtosalmacen` | `set_movtosalmacen` |
| `kpi_mov_almacen` | Agregado sobre `movtosalmacen` | `set_mvakpi` |
| `kpi_mov_inventario` | Agregado sobre `movsinv` | `set_mvikpi` |

### 7.4 Compras, gastos y caja

| Solicitud | Tabla local | Endpoint |
|---|---|---|
| `compras` | `compras` y `comprasmovtos` | `set_compras` |
| `gastos` | `gastos` y `gastosmovtos` | `set_gastos` |
| `tipogastos` | `tipogastos` | `set_tipogastos` |
| `subtipogastos` | `subtipogastos` | `set_subtipogastos` |
| `movscaja` | `movtoscaja` | `set_movscaja` |
| `movsCashType` | Implementación incorrecta | `set_cashMovsType` |
| `configuracion` | `configuracion` | `set_configuration` |

## 8. Evidencia de acceso de solo lectura

El código descompilado contiene dos repositorios:

1. `SQLServerDataRepository`, basado en `System.Data.SqlClient`.
2. `FXDataRepository`, basado en `VFPOLEDB.1` para una variante heredada de Visual FoxPro.

En todo `FService.exe` se verificaron las siguientes ocurrencias:

| Operación | Cantidad encontrada |
|---|---:|
| `ExecuteNonQuery` | 0 |
| `INSERT` | 0 |
| `UPDATE` | 0 |
| `DELETE` | 0 |
| `MERGE` | 0 |
| `DROP` | 0 |
| `ALTER` | 0 |

Por tanto, el ensamblado analizado no modifica la base local mediante SQL. Sí lee una cantidad extensa de información operativa y la transmite a un tercero.

## 9. Hallazgos de seguridad

### 9.1 Críticos

#### Credenciales SQL en texto plano

`Config.xml` almacena directamente:

- Usuario SQL.
- Contraseña SQL.
- Token de la sucursal.
- Servidor y nombre de base.

El archivo distribuido incluye una cuenta `sa` y una contraseña predeterminada. El valor exacto no se reproduce en este documento.

Recomendación:

- Crear una cuenta exclusiva con permisos `SELECT` sobre tablas autorizadas.
- No utilizar `sa`.
- Proteger secretos con DPAPI de Windows o el almacén seguro del sistema.
- Restringir mediante ACL el directorio de configuración.

#### Token recibido mediante URL

El configurador obtiene el token desde el query string de la navegación final. Además, convierte la URL completa a minúsculas antes de leerlo.

Riesgos:

- Exposición en historial, diagnósticos o navegación.
- Corrupción de tokens sensibles a mayúsculas y minúsculas.
- Aceptación de una URL ajena si contiene `done`.

Recomendación:

- Validar esquema, dominio, ruta y estado de navegación.
- Usar un código de activación de un solo uso.
- Intercambiar el código por credenciales mediante un canal HTTPS autenticado.
- Nunca devolver el token permanente en la URL.

### 9.2 Altos

#### Binarios sin firma

El instalador MSI y los dos ejecutables propios no están firmados. Esto dificulta verificar origen e integridad y puede generar advertencias de Windows.

#### Alcance excesivo de datos

El agente puede enviar ventas, pagos, empleados, bitácoras, compras, gastos, inventarios, movimientos de caja y configuración. Debe existir un inventario formal de datos, finalidad, retención y permisos por sucursal.

#### Dependencia de credenciales administrativas

La cadena de conexión utiliza autenticación SQL con usuario y contraseña. No contiene `Application Name`, cifrado explícito, validación de certificado ni intención de solo lectura.

## 10. Hallazgos de confiabilidad

### 10.1 El temporizador no espera la sincronización

El evento del temporizador invoca `MigrateData()` sin hacer `await`. Esto permite:

- Solapamiento de dos sincronizaciones.
- Excepciones no observadas.
- Consumo duplicado de recursos.
- Envío repetido de paquetes.

### 10.2 Errores silenciosos

El evento principal contiene un `catch` vacío. Varios métodos devuelven listas vacías al fallar, lo que hace indistinguibles estos escenarios:

- La tabla no tiene datos.
- La consulta falló.
- La conexión SQL falló.
- Una columna no existe.

### 10.3 No existe cola local

No se encontró una cola persistente para periodos sin Internet. Tampoco existe evidencia de:

- Reintento exponencial.
- Identificador idempotente de paquete.
- Confirmación del servidor.
- Reanudación después de reiniciar Windows.
- Dead-letter queue.

### 10.4 No valida la respuesta del servidor

Los envíos deserializan la respuesta pero no verifican de forma útil:

- Código HTTP.
- Estado funcional.
- Número de registros aceptados.
- Registros rechazados.
- Confirmación de persistencia.

### 10.5 Configuración de tiempos incompleta

Aunque el XML contiene `DbTimeOut` y `WebTimeOut`, los repositorios no aplican el timeout de base de datos y el envío no utiliza correctamente el timeout configurado en el XML.

## 11. Hallazgos de calidad de datos

### 11.1 Conversión de datos a texto

La mayoría de los campos son convertidos mediante `ToString()`. Esto elimina información de tipo y puede producir diferencias por configuración regional en:

- Decimales.
- Fechas.
- Valores booleanos.
- Valores nulos.

### 11.2 Zona horaria fija

Los Unix timestamps recibidos se convierten restando exactamente cinco horas. Esto no representa correctamente Tijuana y tampoco contempla cambios de horario de verano.

Recomendación:

- Conservar UTC en transporte y almacenamiento central.
- Configurar la zona IANA/Windows por sucursal.
- Convertir a hora local solamente para presentación o reglas de día operativo.

### 11.3 Límites de fecha exclusivos

Las consultas usan:

```sql
WHERE fecha > @from AND fecha < @to
```

Un registro exactamente igual a cualquiera de los límites puede quedar fuera. Para sincronización incremental se recomienda una ventana semiabierta:

```sql
WHERE fecha >= @from AND fecha < @to
```

acompañada de llaves idempotentes y una ventana reciente de relectura.

### 11.4 Uso extensivo de `SELECT *`

Aunque posteriormente sólo se copian columnas concretas, las consultas solicitan todas las columnas. Esto aumenta transferencia local, acoplamiento al esquema y riesgo de acceso accidental a campos nuevos.

### 11.5 Sin conciliación

No existe una verificación que compare:

- Cantidad de tickets.
- Total de ventas.
- Propinas.
- Tickets cancelados.
- Líneas.
- Pagos.

Por ello, un paquete omitido o una consulta parcial podría pasar desapercibido.

## 12. Errores funcionales concretos

### 12.1 `gruposi` extrae la entidad equivocada

Cuando la API solicita `gruposi`, el servicio llama a `getTipoGastos()` en lugar de `getGruposi()`.

Consecuencia: `set_gruposi` recibe tipos de gasto y el método correcto queda sin utilizar.

### 12.2 `movsCashType` envía un KPI de inventario

La bandera `movsCashType` llama a `getMovsInvetarioKpi()` en lugar de `getCashMovsType()`.

Consecuencia: `set_cashMovsType` recibe una estructura incompatible.

### 12.3 Nombre de payload mal escrito

Para horarios de turnos se utiliza la clave:

```text
hoariosturnos
```

en lugar de:

```text
horariosturnos
```

### 12.4 Validaciones nulas demasiado tarde

El flujo intenta usar `sucursalResult`, `propResult.Token` y la propiedad `data` antes de confirmar que los objetos existen.

### 12.5 Implementación incompleta de Visual FoxPro

El repositorio `FXDataRepository` incluye métodos no implementados para:

- Subtipos de gastos.
- Configuración.
- Movimientos de caja.
- Tipos de movimiento de caja.

También existen métodos que construyen una conexión sin asignar la cadena correspondiente o intentan ejecutar una consulta antes de abrirla.

## 13. Comparación con el extractor de este proyecto

### 13.1 Ventajas de FIXE

- Instalador terminado.
- Registro automático como servicio de Windows.
- Activación gráfica de sucursal.
- Detección de `restaurant.ini`.
- Comunicación con una API central.
- Envío por paquetes.
- Cobertura amplia de catálogos e inventarios.
- Solicitud remota de conjuntos de datos predefinidos.

### 13.2 Ventajas de nuestro extractor

- .NET 8 en lugar de .NET Framework 4.6.2.
- Consultas explícitas y versionadas.
- Contratos tipados para fechas, decimales y booleanos.
- Separación correcta de encabezados, líneas y pagos.
- Definición de venta histórica válida.
- Conciliación contra totales de control.
- Rango de fechas configurable.
- Extracción determinista a JSON.
- Pruebas previas de idempotencia.
- Diseño para una cuenta SQL dedicada de solo lectura.
- Diferenciación entre ventas históricas y cuentas temporales.

### 13.3 Funciones pendientes en nuestro proyecto

- Catálogo de productos, precios, grupos y formas de pago.
- API central y PostgreSQL.
- Autenticación y activación de agentes.
- Cola persistente SQLite.
- Reintentos y confirmación de paquetes.
- Worker Service de Windows.
- Instalador y actualización controlada.
- Telemetría y estado de sincronización.
- Dashboard web.

## 14. Qué conviene reutilizar conceptualmente

1. Detección de instalaciones conocidas de RestaurantAgent.
2. Lectura controlada de `restaurant.ini`.
3. Asistente de activación de sucursal.
4. Servicio Windows con inicio automático.
5. Manifiesto remoto de entidades solicitadas, limitado a capacidades predefinidas.
6. División de datos en paquetes.
7. Separación entre repositorios por motor de base de datos, sólo si realmente se requiere compatibilidad heredada.

## 15. Qué no debe copiarse

1. Uso de `sa`.
2. Contraseñas o tokens en texto plano.
3. Token permanente en query string.
4. `SELECT *`.
5. Conversión general de valores mediante `ToString()`.
6. Temporizador `async void` sin exclusión mutua.
7. `catch` vacíos.
8. Envíos sin acuse de recibo.
9. Intervalo fijo sin backoff.
10. Zona horaria fija UTC-5.
11. Selección de tablas temporales como si fueran ventas históricas.
12. Paquetes sin identificador idempotente.
13. Instaladores y ejecutables sin firma.

## 16. Arquitectura recomendada para nuestro sistema

```text
RestaurantAgent SQL Server
        |
        | Cuenta SQL exclusiva de solo lectura
        v
.NET 8 Windows Worker Service
        |
        +-- Consultas versionadas y tipadas
        +-- Ventana reciente de relectura
        +-- Conciliación periódica
        +-- Cola SQLite transaccional
        +-- Paquetes con batch_id idempotente
        +-- Secretos protegidos con DPAPI
        |
        | HTTPS saliente
        v
API central
        |
        +-- Autenticación por agente y sucursal
        +-- Validación estricta del contrato
        +-- Upsert idempotente
        +-- Acuse de recibo por paquete
        +-- Auditoría de sincronización
        v
PostgreSQL central
        |
        v
Dashboard web
```

## 17. Contrato sugerido para paquetes

Cada envío debería contener, como mínimo:

```json
{
  "contract_version": 1,
  "agent_id": "agent-uuid",
  "branch_id": "branch-uuid",
  "batch_id": "uuid-idempotente",
  "entity": "sales",
  "range": {
    "from_utc": "2026-08-26T00:00:00Z",
    "to_utc": "2026-08-27T00:00:00Z"
  },
  "sequence": 1,
  "sequence_total": 3,
  "records": [],
  "control": {
    "row_count": 0,
    "amount_sum": 0,
    "content_hash": "sha256"
  }
}
```

La respuesta del servidor debería confirmar:

```json
{
  "batch_id": "uuid-idempotente",
  "accepted": true,
  "inserted": 0,
  "updated": 0,
  "rejected": 0,
  "server_received_at": "2026-08-27T00:00:00Z"
}
```

El agente sólo debe retirar el paquete de SQLite después de recibir y persistir este acuse.

## 18. Orden recomendado de implementación

### Paso 1 — completar el contrato local

- Agregar productos, precios, grupos y formas de pago.
- Mantener consultas explícitas y tipadas.
- Agregar totales de control para las nuevas entidades cuando corresponda.

### Paso 2 — API central

- Activación de agentes.
- Token distinto por sucursal.
- Endpoint idempotente para paquetes.
- PostgreSQL con llaves por sucursal.
- Registro de sincronizaciones y rechazos.

### Paso 3 — agente Windows

- Convertir el extractor en Worker Service.
- Agregar cola SQLite.
- Evitar ejecuciones simultáneas.
- Implementar reintento exponencial y jitter.
- Proteger secretos con DPAPI.
- Reportar salud sin incluir credenciales.

### Paso 4 — instalador

- Detectar `restaurant.ini`.
- Validar conectividad con una consulta mínima.
- Comprobar que la identidad SQL sólo tiene permisos de lectura.
- Activar la sucursal mediante código de un solo uso.
- Instalar, iniciar y verificar el servicio.
- Firmar MSI y ejecutables.

### Paso 5 — dashboard

- Ventas, tickets, ticket promedio y propinas.
- Formas de pago.
- Productos y categorías.
- Cancelaciones y descuentos.
- Turnos, cajeros y movimientos de caja.
- Estado y retraso de cada agente.

## 19. Verificaciones obligatorias antes de producción

### Base de datos

- Confirmar que todas las consultas son `SELECT`.
- Probar con una cuenta SQL restringida.
- Comparar tickets, líneas, pagos y totales con RestaurantAgent.
- Probar cambios tardíos: pago, cierre, cancelación y total.

### Sincronización

- Cortar Internet durante varios ciclos.
- Reiniciar Windows con paquetes pendientes.
- Reenviar el mismo `batch_id` y verificar que no duplique datos.
- Simular timeout y respuesta HTTP 500.
- Simular rechazo parcial de registros.

### Seguridad

- Verificar que ningún secreto aparezca en logs.
- Validar aislamiento entre sucursales.
- Confirmar que el agente no acepta SQL remoto arbitrario.
- Revisar permisos de archivos y servicio.
- Validar firma del instalador y ejecutables.

### Instalación

- Instalar en una máquina limpia.
- Confirmar detección de RestaurantAgent 11.
- Confirmar arranque automático después de reiniciar.
- Desinstalar sin borrar datos de RestaurantAgent ni archivos operativos ajenos.

## 20. Dictamen final

La aplicación FIXE demuestra una solución funcional de la misma familia que el sistema deseado: agente local, servicio Windows, lectura de RestaurantAgent y sincronización a una plataforma central.

Su mayor valor para este proyecto es servir como referencia para:

- Instalación y activación.
- Detección automática de RestaurantAgent.
- Operación continua como servicio.
- Catálogo de entidades sincronizables.
- Envío de información por lotes.

Sin embargo, nuestro desarrollo debe conservar una arquitectura más segura y verificable: consultas versionadas, contratos tipados, cuenta de solo lectura, reconciliación, idempotencia, cola persistente, confirmación del servidor, manejo correcto de zona horaria, trazabilidad y firma de artefactos.

La aplicación analizada confirma el camino arquitectónico, pero no debe convertirse en la especificación técnica literal del nuevo sistema.
