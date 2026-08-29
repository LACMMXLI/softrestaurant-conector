# Plan de desarrollo del dashboard móvil de SoftRestaurant

## 1. Objetivo

Construir una aplicación web móvil, clara y operativa para consultar la información consolidada por el conector de SoftRestaurant. PostgreSQL será la única fuente del dashboard. La aplicación no consultará SQL Server directamente y nunca modificará datos de SoftRestaurant.

El resultado debe poder desplegarse en Coolify junto con la API existente y reutilizar la base PostgreSQL actual, sin crear una base paralela ni cargar datos de demostración.

## 2. Reglas no negociables

1. No mostrar datos inventados, ejemplos ni cifras simuladas en producción.
2. Distinguir un cero real de un dato ausente, atrasado o todavía no sincronizado.
3. Mostrar siempre sucursal, fecha de negocio, zona horaria y última sincronización.
4. No exponer al navegador la llave administrativa ni las credenciales de los conectores.
5. Toda métrica debe tener una consulta definida, una fuente identificada y una prueba de reconciliación.
6. Los importes de venta se calculan desde la cabecera del ticket; nunca se reconstruye la venta total sumando líneas.
7. Los pagos se agregan por ticket antes de relacionarlos con productos para evitar multiplicar importes.
8. Inventario, costos, recetas, compras, proveedores, clientes y facturación quedan fuera mientras no exista información válida y sincronizada.
9. La interfaz debe seguir funcionando con estados vacíos reales: “Sin datos sincronizados”, “Sucursal atrasada” o “Periodo incompleto”.

## 3. Estado real del sistema actual

Ya existe el recorrido:

```text
SoftRestaurant / SQL Server (solo SELECT)
        -> agente Windows .NET 8
        -> cola SQLite
        -> HTTPS autenticado
        -> API central .NET 8
        -> PostgreSQL
```

PostgreSQL ya recibe:

- sucursales y estado de sincronización;
- lotes conciliados;
- cabeceras de venta;
- líneas de venta;
- pagos;
- turnos;
- declaraciones de cajero;
- entradas y salidas de caja;
- cancelaciones de productos agregadas.

La API actual ofrece salud, ingesta, un resumen diario básico y estado de sincronización. Todavía no existe frontend, autenticación para personas ni endpoints completos de reportes.

El endpoint actual `/api/dashboard/today` sólo entrega:

- tickets válidos;
- venta;
- tickets cancelados;
- ticket promedio;
- salidas de caja;
- número de líneas canceladas.

Este endpoint usa credenciales del conector. No debe consumirse directamente desde un navegador público.

## 4. Qué se puede mostrar y qué falta

| Información | Datos actuales | Estado para el dashboard |
|---|---|---|
| Venta, tickets y ticket promedio | `sales` | Disponible |
| Propinas | `sales.tip` y pagos | Disponible, falta endpoint |
| Tendencia por hora | `sales.closed_at` | Disponible, falta endpoint |
| Tickets cancelados | `sales.cancelled` | Disponible, falta endpoint |
| Productos cancelados | `cancellation_summaries` | Disponible |
| Entradas y salidas de caja | `cash_movements` | Disponible |
| Turnos y declaraciones | `shifts`, `cash_declarations` | Disponible, requiere fórmula validada para diferencias |
| Detalle de ticket | `sales.payload`, `sale_lines`, `sale_payments` | Disponible, falta endpoint |
| Formas de pago legibles | Sólo se sincroniza el identificador | Requiere catálogo `formasdepago` |
| Productos y grupos legibles | Las líneas tienen identificador, no nombre/grupo | Requiere catálogos `productos` y `grupos` |
| Meseros y cajeros legibles | La venta conserva identificadores | Requiere catálogos `meseros` y `usuarios` |
| Estaciones y áreas legibles | La venta conserva identificadores | Requiere catálogos `estaciones` y `areasrestaurant` |
| Inventario y costos | La copia analizada no tiene operación válida | Excluido |

No se publicarán tarjetas de “Top productos”, “Venta por grupo”, “Forma de pago”, “Mesero” o “Cajero” con códigos crudos presentados como nombres. Esas secciones se activarán después de sincronizar sus catálogos reales.

## 5. Experiencia móvil

### 5.1 Navegación principal

La aplicación tendrá cuatro destinos inferiores, pensados para uso con una mano:

1. **Inicio**: resumen ejecutivo y frescura de datos.
2. **Ventas**: tendencias, tickets, productos y pagos.
3. **Operación**: turnos, caja y cancelaciones.
4. **Más**: sucursales, sincronización, usuarios y sesión.

En escritorio la misma navegación se convierte en barra lateral, sin crear una aplicación diferente.

### 5.2 Encabezado persistente

- nombre del dashboard;
- selector de sucursal o “Todas” según permisos;
- selector de fecha o rango;
- indicador de última actualización;
- estado: actualizado, atrasado, sin conexión o periodo incompleto.

### 5.3 Inicio

Orden recomendado para una pantalla de 360 a 430 píxeles:

1. Estado de sincronización.
2. Venta del periodo.
3. Tickets y ticket promedio.
4. Propinas.
5. Gráfica de venta por hora.
6. Comparación contra el periodo anterior, sólo cuando ambos periodos estén completos.
7. Formas de pago, después de sincronizar su catálogo.
8. Alertas operativas: cancelaciones, salidas de caja y sucursales atrasadas.
9. Acceso a tickets recientes.

Las tarjetas no deben llenar la pantalla con nueve indicadores iguales. Venta será el dato dominante; tickets, promedio y propina aparecerán como métricas secundarias compactas.

### 5.4 Ventas

- rango rápido: hoy, ayer, 7 días, 30 días y personalizado;
- venta y tickets por día u hora;
- comparación entre sucursales autorizadas;
- desglose de pagos;
- productos más vendidos por cantidad;
- grupos alimento, bebida y otro;
- lista paginada de tickets;
- búsqueda por folio o número de cheque.

La venta por producto en dinero no se mostrará hasta validar si se presentará como importe bruto de líneas o como importe neto después de descuentos. Esa cifra no debe confundirse con la venta contable de cabeceras.

### 5.5 Detalle de ticket

- folio y número de cheque;
- fecha de apertura y cierre;
- estado pagado/cancelado;
- total y propina;
- mesa, área, estación, turno, mesero y usuario de pago cuando existan;
- líneas con cantidad, producto, precio, descuento y comentario;
- pagos separados;
- motivo y usuario de cancelación cuando corresponda.

Los campos ausentes se omiten o se presentan como “No registrado”; nunca se rellenan con nombres genéricos.

### 5.6 Operación

- turnos abiertos y cerrados;
- declaraciones por forma de pago;
- entradas de caja (`tipo = 2`);
- salidas de caja (`tipo = 1`);
- tickets cancelados;
- productos cancelados con usuario y razón disponibles;
- estado de conciliación del último lote.

Una “diferencia de corte” se etiquetará como candidata y permanecerá oculta por defecto hasta compararla con cortes oficiales reales.

### 5.7 Lenguaje visual

- diseño limpio con alto contraste y fondo neutro;
- color verde únicamente para estados correctos;
- ámbar para retrasos o cobertura parcial;
- rojo sólo para cancelaciones, fallos o alertas;
- importes principales entre 28 y 32 px;
- texto base mínimo de 16 px;
- objetivos táctiles de al menos 44 px;
- gráficas simples, etiquetas visibles y sin depender únicamente del color;
- tablas transformadas en tarjetas o filas expandibles en móvil;
- modo oscuro opcional, después de validar primero el modo principal.

## 6. Política contra datos falsos o engañosos

Cada respuesta de reportes incluirá metadatos:

- periodo solicitado;
- sucursales incluidas;
- zona horaria aplicada;
- última sincronización por sucursal;
- último rango recibido;
- conciliación correcta o pendiente;
- cobertura completa, parcial o ausente.

Reglas de presentación:

- `0` se muestra sólo cuando la consulta fue ejecutada sobre un periodo con cobertura completa y el resultado fue realmente cero;
- un valor desconocido se devuelve como `null`, no como cero;
- una sucursal atrasada no se mezcla silenciosamente en el total de “Todas”;
- el total consolidado indicará qué sucursales fueron incluidas y cuáles no;
- si ayer o la semana anterior no tienen cobertura completa, no se calcula porcentaje de comparación;
- la PWA puede conservar sólo la estructura visual; cualquier dato cacheado debe mostrar claramente su hora y condición de desactualizado;
- producción no incluirá seeds, fixtures ni cuentas demo.

## 7. Ampliación del contrato de sincronización

Antes de habilitar todos los reportes se agregarán instantáneas de sólo lectura para:

- productos;
- grupos;
- formas de pago;
- meseros;
- usuarios operativos de SoftRestaurant;
- estaciones;
- áreas de restaurante.

Estas entidades se enviarán como catálogos versionados o instantáneas completas por sucursal. PostgreSQL hará `upsert` por las llaves reales verificadas. Los registros que dejen de aparecer se marcarán inactivos; no se borrará historial de tickets.

Tablas centrales propuestas:

- `products` con sucursal, identificador, descripción, grupo, clasificación y estado;
- `product_groups`;
- `payment_methods` con identificador, descripción, tipo y configuración de conversión necesaria;
- `waiters`;
- `source_users`;
- `stations`;
- `restaurant_areas`.

Los usuarios del dashboard son distintos de los usuarios operativos importados de SoftRestaurant.

## 8. Autenticación y permisos del dashboard

Agregar tablas independientes:

- `app_users`;
- `app_sessions`;
- `user_branch_access`;
- `audit_log`.

Roles iniciales:

- **OWNER**: todas las sucursales, usuarios y configuración;
- **MANAGER**: reportes de las sucursales asignadas;
- **VIEWER**: consulta sin administración ni exportaciones sensibles.

El navegador utilizará una sesión segura mediante cookie `HttpOnly`, `Secure` y `SameSite=Lax`. No se guardarán tokens en `localStorage`.

El primer propietario se crea mediante variables secretas de Coolify y un proceso de inicialización controlado. No habrá contraseña predeterminada. Después del alta, el secreto de bootstrap debe retirarse.

## 9. API de lectura para la aplicación web

Crear un espacio separado `/api/web/*` protegido por sesión humana:

- `POST /api/web/auth/login`
- `POST /api/web/auth/logout`
- `GET /api/web/auth/me`
- `GET /api/web/branches`
- `GET /api/web/dashboard/summary`
- `GET /api/web/dashboard/timeseries`
- `GET /api/web/reports/payments`
- `GET /api/web/reports/products`
- `GET /api/web/reports/cancellations`
- `GET /api/web/reports/cash-movements`
- `GET /api/web/reports/shifts`
- `GET /api/web/sales`
- `GET /api/web/sales/{branchCode}/{folio}`
- `GET /api/web/sync-status`

Todos los endpoints tendrán:

- filtro obligatorio de sucursales autorizadas;
- fechas con límite de rango;
- paginación estable;
- ordenamiento controlado;
- parámetros SQL;
- cancelación por desconexión;
- contrato de error consistente;
- metadatos de cobertura y frescura;
- pruebas que impidan consultar una sucursal no asignada.

La llave `CONNECTOR_ADMIN_KEY`, las claves de activación y los tokens de conectores no forman parte de esta API.

## 10. Fórmulas de negocio iniciales

### Venta válida

```text
paid = true AND cancelled = false AND closed_at IS NOT NULL
```

### Venta

```text
SUM(sales.total) una vez por ticket válido
```

### Tickets

```text
COUNT(*) de tickets válidos
```

### Ticket promedio

```text
SUM(total) / COUNT(*) de tickets válidos
```

### Propina

Se presenta separada de la venta. La fuente primaria se definirá y reconciliará para evitar duplicarla entre cabecera y pagos.

### Pagos

Se usa el detalle `sale_payments` unido a tickets válidos. El importe en moneda base debe considerar `importe * tipodecambio`; actualmente el tipo de cambio está dentro del `payload`, por lo que conviene convertirlo en columna tipada antes del reporte.

### Productos

Se unen líneas a tickets válidos por sucursal y folio. La primera clasificación segura será por cantidad. Los importes brutos de línea se mostrarán sólo con una etiqueta explícita y después de reconciliar descuentos.

### Cancelaciones

- ticket completo: cabeceras con `cancelled = true`;
- producto cancelado: instantáneas de `cancellation_summaries`;
- importe cancelado: fórmula validada con cantidad, precio y ocurrencias, sin inventar un vínculo cuando no existe folio histórico.

### Caja

- salida: `movement_type = 1 AND cancelled = false`;
- entrada: `movement_type = 2 AND cancelled = false`.

## 11. Arquitectura del frontend

Crear `dashboard-web/` con:

- React y TypeScript;
- Vite para compilación;
- enrutamiento protegido;
- cliente de API con tipos;
- manejo central de sesión y sucursal;
- componentes de gráficas accesibles;
- diseño responsive mobile-first;
- manifiesto PWA instalable;
- pruebas unitarias y de flujo crítico.

Estructura sugerida:

```text
dashboard-web/
  src/
    api/
    auth/
    components/
    features/
      dashboard/
      sales/
      payments/
      cancellations/
      cash/
      shifts/
      sync/
    routes/
    styles/
  Dockerfile
  nginx.conf
```

No se agregará una librería visual grande si los componentes necesarios pueden mantenerse pequeños. El objetivo es carga rápida en redes móviles y una interfaz consistente.

## 12. Despliegue en Coolify

La pila final tendrá:

```text
Internet HTTPS
      -> dashboard-web / Nginx
             -> /api/* -> central-api:8080
      -> PostgreSQL interno existente
```

### Contenedor web

`dashboard-web/Dockerfile` será multi-etapa:

1. Node compila los recursos estáticos.
2. Nginx sirve la aplicación.
3. Nginx redirige `/api` al servicio `api` por la red interna.
4. Las rutas del frontend regresan `index.html`.
5. `index.html`, el manifiesto y el service worker no se cachean de forma prolongada.
6. Los archivos con hash se sirven con caché inmutable.

### Compose

Actualizar `docker-compose.yml` y `docker-compose.coolify.yml` para agregar `web`, conservando `api`, PostgreSQL y el volumen actuales. La base no se elimina ni se recrea.

El dominio público se asignará al puerto 80 del servicio `web`. PostgreSQL no tendrá exposición pública y la API se consumirá por el proxy interno del mismo dominio para evitar CORS innecesario.

### Variables mínimas nuevas

- origen público permitido;
- secreto de sesión;
- correo del propietario inicial;
- contraseña inicial como secreto temporal;
- tiempo de expiración de sesión;
- umbral de sincronización atrasada.

Ninguna variable secreta se incluirá en Git.

## 13. Fases de implementación

### Fase 1 — contrato y modelo de datos

- agregar migraciones versionadas;
- tipar `exchange_rate` en pagos;
- sincronizar catálogos reales;
- agregar índices para consultas por sucursal, fecha, folio, producto y pago;
- conservar compatibilidad con lotes actuales;
- probar idempotencia de los nuevos catálogos.

Criterio de salida: dos sincronizaciones iguales no duplican datos y los nombres de catálogo coinciden con SoftRestaurant.

### Fase 2 — autenticación y API de reportes

- usuarios, sesiones, roles y acceso por sucursal;
- endpoints `/api/web/*`;
- consultas con las fórmulas documentadas;
- metadatos de cobertura y frescura;
- pruebas de autorización, rangos y paginación.

Criterio de salida: un usuario no puede consultar otra sucursal y cada KPI se reconcilia mediante SQL de control.

### Fase 3 — interfaz móvil MVP

- login;
- Inicio;
- Ventas y tickets;
- detalle de ticket;
- Pagos;
- Cancelaciones y caja;
- estado de sincronización;
- estados vacío, atrasado, error y sin conexión.

Criterio de salida: los flujos principales funcionan en 360, 390 y 430 px sin desplazamiento horizontal.

### Fase 4 — Docker y Coolify

- Dockerfile del frontend;
- proxy Nginx;
- Compose actualizado;
- health checks;
- migraciones al iniciar;
- dominio HTTPS;
- respaldo previo de PostgreSQL;
- despliegue sin reemplazar el volumen existente.

Criterio de salida: login, dashboard y API funcionan desde el dominio público; PostgreSQL continúa privado y saludable.

### Fase 5 — validación funcional

Durante al menos siete días:

- comparar venta diaria contra el reporte oficial;
- comparar tickets y ticket promedio;
- comparar pagos por forma y tipo de cambio;
- revisar manualmente al menos diez tickets completos;
- revisar cancelaciones completas y de producto;
- comparar entradas y salidas de caja;
- verificar cambio de día y zona horaria;
- probar una sucursal atrasada y recuperación del conector;
- verificar que no existan duplicados después de reenvíos.

Criterio de salida: totales conciliados, autorización verificada, cero datos simulados y recuperación correcta después de una interrupción.

## 14. Pruebas obligatorias

### Backend

- fórmulas monetarias y redondeo;
- filtro de venta válida;
- pago mixto sin multiplicación por líneas;
- moneda y tipo de cambio;
- rangos semiabiertos;
- zona horaria por sucursal;
- paginación sin duplicados;
- acceso por rol y sucursal;
- periodos incompletos;
- lotes repetidos.

### Frontend

- login y cierre de sesión;
- sesión expirada;
- cambio de sucursal;
- selector de fecha;
- cero real frente a dato ausente;
- datos atrasados;
- carga, error y vacío;
- detalle de ticket;
- navegación táctil;
- accesibilidad de textos, colores y controles.

### Despliegue

- build de API y frontend;
- construcción de ambos contenedores;
- arranque con PostgreSQL existente;
- health checks;
- HTTPS;
- cookies seguras;
- ausencia de secretos en imágenes, logs y repositorio;
- restauración de respaldo de PostgreSQL ensayada.

## 15. Definición de terminado

El dashboard se considerará terminado sólo cuando:

1. use datos reales de PostgreSQL sin mocks de producción;
2. tenga autenticación humana y permisos por sucursal;
3. muestre frescura y cobertura de cada cifra;
4. funcione completamente en dispositivos móviles;
5. permita navegar desde un KPI hasta los tickets que lo componen;
6. esté desplegado con HTTPS en Coolify;
7. conserve PostgreSQL privado y el SQL Server de cada sucursal sin exposición externa;
8. sus cifras principales coincidan con SoftRestaurant durante la validación acordada;
9. tenga pruebas automáticas y evidencia del recorrido real en producción;
10. no muestre inventario, catálogos o métricas que no estén respaldados por datos verificados.

## 16. Primer incremento recomendado

El primer incremento debe entregar una ruta vertical completa:

1. login de propietario;
2. selección de sucursal y fecha;
3. venta, tickets, ticket promedio, propina y última sincronización;
4. gráfica por hora;
5. lista de tickets;
6. detalle con líneas y pagos;
7. cancelaciones y salidas de caja;
8. despliegue del frontend mediante Docker en Coolify;
9. comparación de un día completo contra SoftRestaurant.

Después de conciliar ese incremento se habilitan productos, grupos, formas de pago legibles, personal, cortes y comparación consolidada entre sucursales.
